using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Wingman.Core.Operations;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Takes the details pane's place while the batch queue has entries: a title with the count, one
/// numbered entry per operation with its target version, <c>⚡ admin</c> when it needs elevation,
/// and its post command, then how many need elevation and the <c>g</c> and <c>c</c> keys. When the
/// entries outgrow the pane they scroll between the title and the summary, which stay put; with
/// focus the arrow and paging keys scroll them, and the mouse wheel does whenever it is over the pane.
/// </summary>
internal sealed class QueuePane : View, IThemedView
{
    private const string Indent = "    ";
    private const string AdminText = "⚡ admin";
    private const string AdminGap = "  ";
    private const string RunKey = "g";
    private const string RunLabel = " run queue";
    private const string KeyGap = "   ";
    private const string ClearKey = "c";
    private const string ClearLabel = " clear";

    // The title and the blank row under it.
    private const int HeaderRows = 2;
    private const int WheelStep = 3;

    // Measured by Terminal.Gui, which draws ⚡ two cells wide like Windows Terminal does; DisplayWidth counts it as one.
    private static readonly int AdminSuffixWidth = (AdminGap + AdminText).GetColumns();

    private Theme _theme;
    private readonly OperationQueue _queue;
    private int _scroll;
    private int _entryRowsShown;
    private int _entryLineCount;

    // Where the key row was last drawn, for clicks on its keys.
    private int _keyRowY = -1;

    public QueuePane(Theme theme, OperationQueue queue)
    {
        _theme = theme;
        _queue = queue;
        CanFocus = true;
        queue.Changed += SetNeedsDraw;
    }

    /// <summary>Raised on a click on <c>g run queue</c>.</summary>
    public event Action? RunRequested;

    /// <summary>Raised on a click on <c>c clear</c>.</summary>
    public event Action? ClearRequested;

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        _keyRowY = -1;
        if (_queue.Count == 0)
        {
            return true;
        }

        var textWidth = Math.Max(0, Viewport.Width - 1);
        var normal = _theme.On(_theme.Foreground);
        var dim = _theme.On(_theme.Dim);
        var accent = _theme.On(_theme.Accent);

        Move(0, 0);
        SetAttribute(_theme.On(_theme.Foreground, TextStyle.Bold));
        AddStr(" Queue");
        SetAttribute(dim);
        var countText = _queue.Count == 1 ? "1 operation" : $"{_queue.Count} operations";
        AddStr(CellText.Fit("  " + countText, Math.Max(0, textWidth - DisplayWidth.Of(" Queue"))));

        var entryLines = EntryLines(textWidth, normal, accent);
        var summaryLines = SummaryLines();

        // A blank row, the summary, another blank row, and the key row.
        var footerRows = 1 + summaryLines.Count + 2;
        var roomForEntries = Math.Max(0, Viewport.Height - HeaderRows - footerRows);
        _entryLineCount = entryLines.Count;
        _entryRowsShown = Math.Min(entryLines.Count, roomForEntries);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll());

        var y = HeaderRows;
        for (var i = 0; i < _entryRowsShown; i++)
        {
            var segments = entryLines[_scroll + i];
            Move(0, y);
            foreach (var (text, color) in segments)
            {
                SetAttribute(color);
                AddStr(text);
            }

            y++;
        }

        y++;
        foreach (var line in summaryLines)
        {
            Move(0, y);
            SetAttribute(dim);
            AddStr(CellText.Fit(line, textWidth));
            y++;
        }

        y++;
        DrawKeyRow(y, normal);
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
            scrollTo = _scroll + Math.Max(1, _entryRowsShown - 1);
        }
        else if (key == Key.PageUp)
        {
            scrollTo = _scroll - Math.Max(1, _entryRowsShown - 1);
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

        var isOnKeyRow = mouse.Position is { } position && position.Y == _keyRowY;
        if (isOnKeyRow && mouse.IsLeftClick())
        {
            var x = mouse.Position!.Value.X;
            var runEnd = 1 + DisplayWidth.Of(RunKey + RunLabel);
            var clearStart = runEnd + KeyGap.Length;
            var clearEnd = clearStart + DisplayWidth.Of(ClearKey + ClearLabel);
            if (x >= 1 && x < runEnd)
            {
                RunRequested?.Invoke();
                return true;
            }

            if (x >= clearStart && x < clearEnd)
            {
                ClearRequested?.Invoke();
                return true;
            }
        }

        return base.OnMouseEvent(mouse);
    }

    /// <summary>
    /// Each operation as <c> 1  Git.Git</c>, then <c>    upgrade → 2.52.0  ⚡ admin</c>, then
    /// <c>    post: …</c> when it has a post command; every line is a list of colored segments.
    /// </summary>
    private List<List<(string Text, Attribute Color)>> EntryLines(int textWidth, Attribute normal, Attribute accent)
    {
        var lines = new List<List<(string Text, Attribute Color)>>();
        for (var i = 0; i < _queue.Items.Count; i++)
        {
            var item = _queue.Items[i];
            var number = $" {i + 1}";
            var numberColumn = number.PadRight(Math.Max(Indent.Length, number.Length + 1));
            lines.Add([(CellText.Fit(numberColumn + item.Row.Id, textWidth), normal)]);

            var action = Indent + ActionText(item);
            if (item.Plan.RequiresElevation)
            {
                var fitted = CellText.Fit(action, Math.Max(0, textWidth - AdminSuffixWidth));
                lines.Add([(fitted + AdminGap, normal), (AdminText, accent)]);
            }
            else
            {
                lines.Add([(CellText.Fit(action, textWidth), normal)]);
            }

            if (item.Plan.PostCommand.Length > 0)
            {
                lines.Add([(CellText.Fit(Indent + "post: " + item.Plan.PostCommand, textWidth), normal)]);
            }
        }

        return lines;
    }

    /// <summary><c>upgrade → 2.52.0</c>, <c>install → latest</c>, or plain <c>uninstall</c>.</summary>
    private static string ActionText(QueuedOperation item)
    {
        var verb = item.Kind.ToString().ToLowerInvariant();
        if (item.Kind == OperationKind.Uninstall)
        {
            return verb;
        }

        var target = item.Row.AvailableVersion;
        if (string.IsNullOrEmpty(target))
        {
            target = item.Plan.Request.Version;
        }

        if (string.IsNullOrEmpty(target))
        {
            target = "latest";
        }

        return $"{verb} → {target}";
    }

    private List<string> SummaryLines()
    {
        var elevated = _queue.ElevatedCount;
        if (elevated == 0)
        {
            return [" No elevation needed."];
        }

        return [$" {elevated} of {_queue.Count} need elevation.", " One UAC prompt will be shown."];
    }

    /// <summary>Draws <c> g run queue   c clear</c>, keys in accent bold.</summary>
    private void DrawKeyRow(int y, Attribute normal)
    {
        if (y >= Viewport.Height)
        {
            return;
        }

        _keyRowY = y;
        var key = _theme.On(_theme.Accent, TextStyle.Bold);
        Move(0, y);
        SetAttribute(normal);
        AddStr(" ");
        SetAttribute(key);
        AddStr(RunKey);
        SetAttribute(normal);
        AddStr(RunLabel + KeyGap);
        SetAttribute(key);
        AddStr(ClearKey);
        SetAttribute(normal);
        AddStr(ClearLabel);
    }

    private int MaxScroll() => Math.Max(0, _entryLineCount - _entryRowsShown);

    private void ScrollTo(int target)
    {
        var clamped = Math.Clamp(target, 0, MaxScroll());
        if (clamped != _scroll)
        {
            _scroll = clamped;
            SetNeedsDraw();
        }
    }
}
