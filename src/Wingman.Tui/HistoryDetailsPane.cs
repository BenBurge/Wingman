using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// The History tab's right pane for the cursor row: the operation and package in bold, the exit code
/// and when it ran, then the stored log word-wrapped, after what the exit code usually means when
/// the operation failed. The keys the row takes are on the last line. With focus, the arrow and
/// paging keys scroll the log; the mouse wheel scrolls it whenever it is over the pane.
/// </summary>
internal sealed class HistoryDetailsPane : View, IThemedView
{
    // The title, the exit code line, and the blank row under them.
    private const int BodyTop = 3;

    // The blank row above the key line, and the key line itself.
    private const int FooterRows = 2;
    private const int WheelStep = 3;

    private Theme _theme;
    private HistoryRow? _row;

    // The body's lines before wrapping.
    private List<(string Text, BodyColor Color)> _body = [];

    private int _wrappedWidth = -1;
    private List<(string Text, BodyColor Color)> _wrappedLines = [];
    private int _scroll;
    private int _rowsShown;

    public HistoryDetailsPane(Theme theme)
    {
        _theme = theme;
        CanFocus = true;
    }

    /// <summary>Whether the pane offers <c>R retry</c> for a row; never when unset.</summary>
    public Func<HistoryRow, bool>? CanRetry { get; set; }

    /// <summary>Shows <paramref name="row"/> with its log from <paramref name="log"/>, or nothing when it is null.</summary>
    public void Show(HistoryRow? row, string log)
    {
        _row = row;
        _body = row is null ? [] : BodyLines(row, log);
        _wrappedWidth = -1;
        _scroll = 0;
        SetNeedsDraw();
    }

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_row is not { } row)
        {
            return true;
        }

        var entry = row.Entry;
        var width = Viewport.Width;
        var dim = _theme.On(_theme.Dim);

        var title = $" {entry.Operation} {row.Package}";
        DrawText(0, 0, CellText.Fit(title, width - 1), _theme.On(_theme.Foreground, TextStyle.Bold));

        var (outcome, outcomeColor) = row.Result switch
        {
            HistoryResult.Ok => ("exit code 0", _theme.On(_theme.Ok)),
            HistoryResult.Canceled => ("canceled", dim),
            _ => ($"exit code {WingetErrorCodes.Format(entry.ExitCode)}", _theme.On(_theme.Error)),
        };
        var outcomeText = " " + outcome;
        DrawText(0, 1, outcomeText, outcomeColor);
        SetAttribute(dim);
        AddStr(CellText.Fit("  " + row.When, width - 1 - DisplayWidth.Of(outcomeText)));

        DrawBody(width);
        DrawFooter(row);
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
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
            scrollTo = _scroll + Math.Max(1, _rowsShown - 1);
        }
        else if (key == Key.PageUp)
        {
            scrollTo = _scroll - Math.Max(1, _rowsShown - 1);
        }
        else if (key == Key.Home)
        {
            scrollTo = 0;
        }
        else if (key == Key.End)
        {
            scrollTo = int.MaxValue;
        }

        // Left and right do nothing here, but left unhandled they would move focus out of the pane.
        var isSidewaysKey = key == Key.CursorLeft || key == Key.CursorRight;
        if (scrollTo is { } target)
        {
            ScrollTo(target);
            return true;
        }

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
    /// The winget command line, then the log; a failed operation's decoded exit code comes first.
    /// A batch's log is its summary, and its exit code only says something in it failed, so it
    /// gets no decoding.
    /// </summary>
    private static List<(string Text, BodyColor Color)> BodyLines(HistoryRow row, string log)
    {
        var entry = row.Entry;
        var lines = new List<(string Text, BodyColor Color)>();
        if (row.Result == HistoryResult.Failed && !row.IsBatch)
        {
            var explanation = WingetErrorCodes.Explain(entry.ExitCode);
            lines.Add(("Usually means", BodyColor.Header));
            lines.Add((explanation.UsuallyMeans, BodyColor.Dim));
            lines.Add(("Suggestion", BodyColor.Header));
            lines.Add((explanation.Suggestion, BodyColor.Dim));
            lines.Add(("", BodyColor.Normal));
        }

        if (entry.Arguments.Count > 0)
        {
            lines.Add(("$ winget " + string.Join(' ', entry.Arguments), BodyColor.Dim));
        }

        foreach (var line in log.TrimEnd('\n').Split('\n'))
        {
            lines.Add((line.TrimEnd('\r'), BodyColor.Normal));
        }

        return lines;
    }

    private void DrawBody(int width)
    {
        var textWidth = Math.Max(1, width - 2);
        if (textWidth != _wrappedWidth)
        {
            _wrappedWidth = textWidth;
            _wrappedLines = [];
            foreach (var (text, color) in _body)
            {
                foreach (var wrapped in CellText.Wrap(text, textWidth))
                {
                    _wrappedLines.Add((wrapped, color));
                }
            }
        }

        _rowsShown = Math.Max(0, Viewport.Height - BodyTop - FooterRows);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll());
        for (var i = 0; i < _rowsShown; i++)
        {
            var index = _scroll + i;
            if (index >= _wrappedLines.Count)
            {
                break;
            }

            var (text, color) = _wrappedLines[index];
            DrawText(1, BodyTop + i, text, AttributeFor(color));
        }
    }

    /// <summary><c> R retry   o edit options</c>, keys in accent, for what the row allows.</summary>
    private void DrawFooter(HistoryRow row)
    {
        var keys = new List<(string Key, string Label)>();
        if (CanRetry?.Invoke(row) ?? false)
        {
            keys.Add(("R", "retry"));
        }

        if (!row.IsBatch)
        {
            keys.Add(("o", "edit options"));
        }

        Move(0, Viewport.Height - 1);
        var normal = _theme.On(_theme.Foreground);
        SetAttribute(normal);
        AddStr(" ");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                SetAttribute(normal);
                AddStr("   ");
            }

            SetAttribute(_theme.On(_theme.Accent, TextStyle.Bold));
            AddStr(keys[i].Key);
            SetAttribute(normal);
            AddStr(" " + keys[i].Label);
        }
    }

    private Attribute AttributeFor(BodyColor color) => color switch
    {
        BodyColor.Header => _theme.On(_theme.Header),
        BodyColor.Dim => _theme.On(_theme.Dim),
        _ => _theme.On(_theme.Foreground),
    };

    private void DrawText(int x, int y, string text, Attribute color)
    {
        SetAttribute(color);
        Move(x, y);
        AddStr(text);
    }

    private int MaxScroll() => Math.Max(0, _wrappedLines.Count - _rowsShown);

    private void ScrollTo(int target)
    {
        var clamped = Math.Clamp(target, 0, MaxScroll());
        if (clamped != _scroll)
        {
            _scroll = clamped;
            SetNeedsDraw();
        }
    }

    private enum BodyColor
    {
        Normal,
        Dim,
        Header,
    }
}
