using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Wingman.Core.Elevation;
using Wingman.Core.Operations;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Wingman.Tui;

/// <summary>
/// Takes the details pane's place while the batch queue has entries: a title with the count, one
/// numbered entry per operation with its target version, <c>⚡ admin</c> when it needs elevation
/// under the settings' elevation mode, and its post command, then how many need elevation, how
/// the batch will get it, and the <c>g</c> and <c>c</c> keys. When the entries outgrow the pane
/// they scroll between the title and the summary, which stay put; with focus the arrow and paging
/// keys scroll them, and the mouse wheel does whenever it is over the pane.
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
    private readonly WingmanSettings _settings;
    private readonly bool _processIsElevated;
    private int _scroll;
    private int _entryRowsShown;
    private int _entryLineCount;

    // Where the key row was last drawn, for clicks on its keys.
    private int _keyRowY = -1;

    /// <param name="settings">The shell's settings, whose elevation mode is read at every draw.</param>
    /// <param name="processIsElevated">Whether this process already runs as administrator.</param>
    public QueuePane(Theme theme, OperationQueue queue, WingmanSettings settings, bool processIsElevated)
    {
        _theme = theme;
        _queue = queue;
        _settings = settings;
        _processIsElevated = processIsElevated;
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
        var summaryRows = SummaryRows(SummaryLines(dim), textWidth);

        // A blank row, the summary, another blank row, and the key row.
        var footerRows = 1 + summaryRows.Count + 2;
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
        foreach (var (line, color) in summaryRows)
        {
            Move(0, y);
            SetAttribute(color);
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
            if (NeedsElevation(item))
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

        // A version picked for the operation wins over the newest one winget offers.
        var target = item.Plan.Request.Version;
        if (string.IsNullOrEmpty(target))
        {
            target = item.Row.AvailableVersion;
        }

        if (string.IsNullOrEmpty(target))
        {
            target = "latest";
        }

        return $"{verb} → {target}";
    }

    /// <summary>
    /// Whether <paramref name="item"/> needs administrator rights under the elevation mode, as an
    /// unelevated process would run it: an elevated one still has it run as administrator, only
    /// without the helper.
    /// </summary>
    private bool NeedsElevation(QueuedOperation item) =>
        ElevationPolicy.UsesHelper(item.Plan, _settings.ElevationMode, processIsElevated: false);

    /// <summary>How many entries need elevation, then how the batch will get it, each line with its color.</summary>
    private List<(string Text, Attribute Color)> SummaryLines(Attribute dim)
    {
        if (_settings.ElevationMode == ElevationMode.Never && !_processIsElevated)
        {
            return [("elevation off (winget will prompt per installer)", dim)];
        }

        var elevated = _queue.Items.Count(NeedsElevation);
        var countLine = elevated == 0 ? "No elevation needed." : $"{elevated} of {_queue.Count} need elevation.";
        if (_processIsElevated)
        {
            return [(countLine, dim), ("elevated helper: running as administrator", _theme.On(_theme.Ok))];
        }

        if (_settings.ElevationMode == ElevationMode.Always)
        {
            return [(countLine, dim), ("every operation runs elevated · one UAC prompt", dim)];
        }

        return elevated == 0 ? [(countLine, dim)] : [(countLine, dim), ("One UAC prompt will be shown.", dim)];
    }

    /// <summary>
    /// <paramref name="lines"/> wrapped to the pane one cell in from its edge, since at 96 columns
    /// the pane is narrower than the longest of them.
    /// </summary>
    private static List<(string Text, Attribute Color)> SummaryRows(List<(string Text, Attribute Color)> lines, int textWidth)
    {
        var rows = new List<(string Text, Attribute Color)>();
        foreach (var (text, color) in lines)
        {
            foreach (var row in CellText.Wrap(text, Math.Max(1, textWidth - 1)))
            {
                rows.Add((" " + row, color));
            }
        }

        return rows;
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
