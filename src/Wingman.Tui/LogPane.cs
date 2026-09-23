using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Takes the details pane's place while an operation runs and after it ends: a title row with the
/// operation's state, the winget command line, then winget's output, following the newest line
/// unless scrolled up. With focus, the arrow and paging keys scroll it, Esc asks to cancel while
/// running, and Enter or Esc go back once it is over; the mouse wheel scrolls it whenever it is
/// over the pane.
/// </summary>
internal sealed class LogPane : View
{
    // The title row, the command line, and a blank row.
    private const int HeaderRows = 3;
    private const int WheelStep = 3;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly Theme _theme;
    private readonly List<(string Text, Attribute Color)> _lines = [];

    private string _title = "";
    private string _commandLine = "";
    private bool? _succeeded;
    private object? _spinnerTimer;
    private int _spinnerFrame;
    private int _scroll;

    // True while the view shows the newest line, so new lines keep it at the bottom.
    private bool _isFollowing = true;

    public LogPane(Theme theme)
    {
        _theme = theme;
        CanFocus = true;
    }

    /// <summary>Raised on Esc while the operation runs.</summary>
    public event Action? CancelRequested;

    /// <summary>Raised on Enter or Esc once the operation is over.</summary>
    public event Action? BackRequested;

    public bool IsRunning => _succeeded is null && _title.Length > 0;

    /// <summary>Clears the log and shows <paramref name="title"/>, such as <c>upgrade GitHub.cli</c>, as running.</summary>
    public void Begin(string title, string commandLine)
    {
        _title = title;
        _commandLine = commandLine;
        _succeeded = null;
        _lines.Clear();
        _scroll = 0;
        _isFollowing = true;

        StopSpinner();
        if (App is { } app)
        {
            _spinnerFrame = 0;
            _spinnerTimer = app.AddTimeout(TimeSpan.FromMilliseconds(100), AdvanceSpinner);
        }

        SetNeedsDraw();
    }

    public void Append(string line) => AddLine(LastSegment(line), _theme.On(_theme.Foreground));

    /// <summary>Marks the operation over and adds <paramref name="verdict"/>, in Ok or Error, as the last line.</summary>
    public void Finish(bool succeeded, string verdict)
    {
        _succeeded = succeeded;
        StopSpinner();
        AddLine(verdict, _theme.On(succeeded ? _theme.Ok : _theme.Error));
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var textWidth = Math.Max(0, width - 2);

        var (glyph, glyphColor) = _succeeded switch
        {
            null => ("▶", _theme.Accent),
            true => ("✓", _theme.Ok),
            false => ("✗", _theme.Error),
        };
        var spinnerWidth = IsRunning ? 2 : 0;
        SetAttribute(_theme.On(glyphColor));
        Move(1, 0);
        AddStr(glyph);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(" " + CellText.Fit(_title, textWidth - 2 - spinnerWidth));
        if (IsRunning)
        {
            SetAttribute(_theme.On(_theme.Accent));
            Move(width - 2, 0);
            AddStr(SpinnerFrames[_spinnerFrame]);
        }

        SetAttribute(_theme.On(_theme.Dim));
        Move(1, 1);
        AddStr(CellText.Fit("$ " + _commandLine, textWidth));

        // Recomputed here because the height can change after the last line arrived.
        var bodyRows = BodyRows();
        _scroll = _isFollowing ? MaxScroll() : Math.Clamp(_scroll, 0, MaxScroll());
        for (var row = 0; row < bodyRows; row++)
        {
            var index = _scroll + row;
            if (index >= _lines.Count)
            {
                break;
            }

            var (text, color) = _lines[index];
            SetAttribute(color);
            Move(1, HeaderRows + row);
            AddStr(CellText.Fit(text, textWidth));
        }

        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            if (IsRunning)
            {
                CancelRequested?.Invoke();
            }
            else
            {
                BackRequested?.Invoke();
            }

            return true;
        }

        if (key == Key.Enter)
        {
            if (!IsRunning)
            {
                BackRequested?.Invoke();
            }

            return true;
        }

        int? scrollTo = null;
        if (key == Key.CursorDown)
        {
            scrollTo = _scroll + 1;
        }
        else if (key == Key.CursorUp)
        {
            scrollTo = _scroll - 1;
        }
        else if (key == Key.PageDown)
        {
            scrollTo = _scroll + Math.Max(1, BodyRows() - 1);
        }
        else if (key == Key.PageUp)
        {
            scrollTo = _scroll - Math.Max(1, BodyRows() - 1);
        }
        else if (key == Key.Home)
        {
            scrollTo = 0;
        }
        else if (key == Key.End)
        {
            scrollTo = int.MaxValue;
        }

        if (scrollTo is { } target)
        {
            ScrollTo(target);
            return true;
        }

        // Left and right do nothing here, but left unhandled they would move focus out of the pane.
        var isSidewaysKey = key == Key.CursorLeft || key == Key.CursorRight;
        return isSidewaysKey || base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            ScrollTo(_scroll + WheelStep);
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            ScrollTo(_scroll - WheelStep);
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    /// <summary>
    /// What a terminal would leave on screen for a line that redraws itself with carriage
    /// returns, as winget's progress bars do: the text after the last one.
    /// </summary>
    private static string LastSegment(string line)
    {
        var trimmed = line.TrimEnd('\r');
        return trimmed[(trimmed.LastIndexOf('\r') + 1)..];
    }

    private void AddLine(string text, Attribute color)
    {
        _lines.Add((text, color));
        SetNeedsDraw();
    }

    private int BodyRows() => Math.Max(0, Viewport.Height - HeaderRows);

    private int MaxScroll() => Math.Max(0, _lines.Count - BodyRows());

    private void ScrollTo(int target)
    {
        var maxScroll = MaxScroll();
        _scroll = Math.Clamp(target, 0, maxScroll);
        _isFollowing = _scroll == maxScroll;
        SetNeedsDraw();
    }

    private bool AdvanceSpinner()
    {
        if (!IsRunning)
        {
            _spinnerTimer = null;
            return false;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
        SetNeedsDraw();
        return true;
    }

    private void StopSpinner()
    {
        if (_spinnerTimer is not null)
        {
            App?.RemoveTimeout(_spinnerTimer);
            _spinnerTimer = null;
        }
    }
}
