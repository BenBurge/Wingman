using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Wingman.Core.Winget;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace Wingman.Tui;

/// <summary>Which theme color a <see cref="CellSpan"/> draws in, resolved when drawn so a theme switch recolors it.</summary>
internal enum Tone
{
    Normal,
    Dim,
    Accent,
    Ok,
    Info,
}

/// <summary>A run of text in one color inside a <see cref="CheckTable"/> cell.</summary>
internal sealed record CellSpan(string Text, Tone Tone = Tone.Normal);

/// <summary>
/// A column of a <see cref="CheckTable"/> after the checkbox column. <paramref name="Width"/> counts
/// the one-cell gap after the text; zero makes the column fill what the others leave.
/// </summary>
internal sealed record CheckColumn(string Header, int Width);

/// <summary>One row of a <see cref="CheckTable"/>: whether it can be checked, and each column's spans.</summary>
internal sealed record CheckRow(bool IsSelectable, IReadOnlyList<IReadOnlyList<CellSpan>> Cells);

/// <summary>
/// The list the bundle screens use: a header row, then one row per entry with a <c>[x]</c> checkbox,
/// or a dim <c>⊘</c> for a row that cannot be checked, and cells made of colored spans, which a
/// <c>TableView</c> cannot draw. The cursor row is drawn background-on-accent across the whole
/// width; a last line reads <c>… 49 more</c> while rows are hidden below. Space or a click on the
/// checkbox toggles a row, a double-click anywhere on it too, and the wheel scrolls.
/// </summary>
internal sealed class CheckTable : View, IThemedView
{
    public const int CheckWidth = 5;

    private const int HeaderRows = 1;
    private const int WheelStep = 3;

    private readonly IReadOnlyList<CheckColumn> _columns;
    private Theme _theme;
    private IReadOnlyList<CheckRow> _rows = [];
    private bool[] _checked = [];
    private int _cursor;
    private int _offset;

    public CheckTable(Theme theme, IReadOnlyList<CheckColumn> columns)
    {
        _theme = theme;
        _columns = columns;
        CanFocus = true;
    }

    /// <summary>Raised after Space, a click, or <see cref="ToggleCursorRow"/> toggles a row; <see cref="SetRows"/> and <see cref="CheckWhere"/> do not raise it.</summary>
    public event Action? Toggled;

    public int Count => _rows.Count;

    /// <summary>The cursor row's index, or -1 when there are no rows.</summary>
    public int CursorIndex => _rows.Count == 0 ? -1 : _cursor;

    /// <summary>Replaces the rows, checking each selectable one <paramref name="isChecked"/> picks; the cursor stays at its index.</summary>
    public void SetRows(IReadOnlyList<CheckRow> rows, Func<int, bool> isChecked)
    {
        _rows = rows;
        _checked = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            _checked[i] = rows[i].IsSelectable && isChecked(i);
        }

        _cursor = Math.Clamp(_cursor, 0, Math.Max(0, rows.Count - 1));
        _offset = Math.Clamp(_offset, 0, MaxOffset());
        EnsureCursorVisible();
        SetNeedsDraw();
    }

    public bool IsChecked(int index) => _checked[index];

    /// <summary>Checks every selectable row <paramref name="predicate"/> picks and unchecks the rest.</summary>
    public void CheckWhere(Func<int, bool> predicate)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            _checked[i] = _rows[i].IsSelectable && predicate(i);
        }

        SetNeedsDraw();
    }

    /// <summary>Toggles the cursor row when it can be checked, as Space does.</summary>
    public void ToggleCursorRow() => Toggle(_cursor);

    public void ApplyTheme(Theme theme) => _theme = theme;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        DrawHeader(width);

        var visibleRows = VisibleRows(_offset);
        for (var line = 0; line < visibleRows && _offset + line < _rows.Count; line++)
        {
            DrawRow(_offset + line, HeaderRows + line, width);
        }

        var hiddenBelow = _rows.Count - _offset - visibleRows;
        if (hiddenBelow > 0)
        {
            Move(0, HeaderRows + visibleRows);
            SetAttribute(_theme.On(_theme.Dim));
            AddStr(CellText.Fit($" … {hiddenBelow} more", width));
        }

        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        var page = Math.Max(1, VisibleRows(_offset));
        if (key == Key.CursorUp)
        {
            MoveCursor(_cursor - 1);
        }
        else if (key == Key.CursorDown)
        {
            MoveCursor(_cursor + 1);
        }
        else if (key == Key.PageUp)
        {
            MoveCursor(_cursor - page);
        }
        else if (key == Key.PageDown)
        {
            MoveCursor(_cursor + page);
        }
        else if (key == Key.Home || key == Key.Home.WithCtrl)
        {
            MoveCursor(0);
        }
        else if (key == Key.End || key == Key.End.WithCtrl)
        {
            MoveCursor(_rows.Count - 1);
        }
        else if (key == Key.Space)
        {
            ToggleCursorRow();
        }
        else
        {
            return base.OnKeyDown(key);
        }

        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        if (mouse.IsWheel)
        {
            var step = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? WheelStep : -WheelStep;
            _offset = Math.Clamp(_offset + step, 0, MaxOffset());
            SetNeedsDraw();
            return true;
        }

        if (!mouse.IsLeftClick())
        {
            return base.OnMouseEvent(mouse);
        }

        SetFocus();
        var line = position.Y - HeaderRows;
        var index = _offset + line;
        var isOnRow = line >= 0 && line < VisibleRows(_offset) && index < _rows.Count;
        if (!isOnRow)
        {
            return true;
        }

        MoveCursor(index);
        var isOnCheckbox = position.X < CheckWidth;
        if (isOnCheckbox || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            Toggle(index);
        }

        return true;
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        _offset = Math.Clamp(_offset, 0, MaxOffset());
        EnsureCursorVisible();
    }

    private static string Pad(string text, int width) => text + new string(' ', Math.Max(0, width - DisplayWidth.Of(text)));

    private int Capacity => Math.Max(1, Viewport.Height - HeaderRows);

    /// <summary>How many rows show from <paramref name="offset"/>: one fewer than fit when the last line is needed for <c>… N more</c>.</summary>
    private int VisibleRows(int offset)
    {
        var capacity = Capacity;
        var hasMoreBelow = _rows.Count - offset > capacity;
        return hasMoreBelow ? Math.Max(1, capacity - 1) : capacity;
    }

    private int MaxOffset() => Math.Max(0, _rows.Count - Capacity);

    private void MoveCursor(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _cursor = Math.Clamp(index, 0, _rows.Count - 1);
        EnsureCursorVisible();
        SetNeedsDraw();
    }

    private void EnsureCursorVisible()
    {
        if (_cursor < _offset)
        {
            _offset = _cursor;
        }

        while (_offset < MaxOffset() && _cursor >= _offset + VisibleRows(_offset))
        {
            _offset++;
        }
    }

    private void Toggle(int index)
    {
        var canToggle = index >= 0 && index < _rows.Count && _rows[index].IsSelectable;
        if (!canToggle)
        {
            return;
        }

        _checked[index] = !_checked[index];
        SetNeedsDraw();
        Toggled?.Invoke();
    }

    /// <summary>Each column's width at a table <paramref name="width"/> cells wide, the fill column taking what is left.</summary>
    private int[] ColumnWidths(int width)
    {
        var fixedWidth = CheckWidth;
        foreach (var column in _columns)
        {
            fixedWidth += Math.Max(0, column.Width);
        }

        var widths = new int[_columns.Count];
        for (var i = 0; i < _columns.Count; i++)
        {
            widths[i] = _columns[i].Width > 0 ? _columns[i].Width : Math.Max(1, width - fixedWidth);
        }

        return widths;
    }

    private void DrawHeader(int width)
    {
        Move(0, 0);
        SetAttribute(_theme.On(_theme.Header));
        var header = new string(' ', CheckWidth);
        var widths = ColumnWidths(width);
        for (var i = 0; i < _columns.Count; i++)
        {
            header += Pad(CellText.Fit(_columns[i].Header, widths[i] - 1), widths[i]);
        }

        AddStr(Pad(CellText.Fit(header, width), width));
    }

    private void DrawRow(int index, int y, int width)
    {
        var row = _rows[index];
        var isCursorRow = index == _cursor;
        Attribute ColorOf(Tone tone) => isCursorRow ? _theme.Selected : _theme.On(ToneColor(tone));

        Move(0, y);
        if (row.IsSelectable)
        {
            SetAttribute(ColorOf(Tone.Normal));
            AddStr("[");
            SetAttribute(ColorOf(Tone.Accent));
            AddStr(_checked[index] ? "x" : " ");
            SetAttribute(ColorOf(Tone.Normal));
            AddStr("]  ");
        }
        else
        {
            SetAttribute(ColorOf(Tone.Dim));
            AddStr(" ⊘   ");
        }

        var x = CheckWidth;
        var widths = ColumnWidths(width);
        for (var i = 0; i < _columns.Count && x < width; i++)
        {
            var columnWidth = Math.Min(widths[i], width - x);
            var spans = i < row.Cells.Count ? row.Cells[i] : [];
            DrawCell(spans, x, y, columnWidth, ColorOf);
            x += columnWidth;
        }

        if (x < width)
        {
            Move(x, y);
            SetAttribute(ColorOf(Tone.Normal));
            AddStr(new string(' ', width - x));
        }
    }

    /// <summary>Draws the spans in <paramref name="width"/> cells, one kept free for the gap, cutting the last one that does not fit with <c>…</c>.</summary>
    private void DrawCell(IReadOnlyList<CellSpan> spans, int x, int y, int width, Func<Tone, Attribute> colorOf)
    {
        Move(x, y);
        var budget = Math.Max(0, width - 1);
        var used = 0;
        foreach (var span in spans)
        {
            if (used >= budget)
            {
                break;
            }

            var text = CellText.Fit(span.Text, budget - used);
            SetAttribute(colorOf(span.Tone));
            AddStr(text);
            used += DisplayWidth.Of(text);
        }

        SetAttribute(colorOf(Tone.Normal));
        AddStr(new string(' ', Math.Max(0, width - used)));
    }

    private Color ToneColor(Tone tone) => tone switch
    {
        Tone.Dim => _theme.Dim,
        Tone.Accent => _theme.Accent,
        Tone.Ok => _theme.Ok,
        Tone.Info => _theme.Info,
        _ => _theme.Foreground,
    };
}
