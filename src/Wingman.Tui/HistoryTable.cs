using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Winget;
using Color = Terminal.Gui.Drawing.Color;

namespace Wingman.Tui;

/// <summary>
/// The History tab's list: a filter box and count on the first row, a blank row, then one row per
/// history entry, newest first, with the result in the success, failure, or dim color. The filter
/// matches each entry's package Id and operation.
/// </summary>
internal sealed class HistoryTable : View, IThemedView
{
    private const int MinCountWidth = 14;
    private const int WheelStep = 3;
    private const string EmptyText = "No operations yet";
    private const int ResultColumn = 3;

    // Widths include the one-cell gap after the text; zero fills what the others leave.
    private static readonly (string Header, Func<HistoryRow, string> Value, int Width)[] Columns =
    [
        ("When", row => row.When, 17),
        ("Operation", row => row.Entry.Operation, 11),
        ("Package", row => row.Package, 0),

        // One wider than the mockup's 8 so "canceled" fits.
        ("Result", row => row.ResultText, 9),

        // Untitled, as in the mockup: "Duration" is wider than the column.
        ("", row => row.DurationText, 6),
    ];

    private readonly Label _promptLabel;
    private readonly TextField _filterField;
    private readonly Label _countLabel;
    private readonly KeyPassingTableView _tableView;
    private readonly Label _emptyLabel;
    private readonly int[] _textWidths = new int[Columns.Length];

    private Theme _theme;
    private IReadOnlyList<HistoryRow> _allRows = [];
    private Source _source;
    private bool _isLoaded;
    private int _columnLayoutWidth = -1;
    private HistoryRow? _lastCursorRow;

    public HistoryTable(Theme theme)
    {
        _theme = theme;
        CanFocus = true;

        _promptLabel = new Label { X = 0, Y = 0, Text = " Filter: " };

        _filterField = new TextField
        {
            X = Pos.Right(_promptLabel),
            Y = 0,
            Width = Dim.Fill(MinCountWidth + 1),
        };
        _filterField.TextChanged += (_, _) => ApplyView();
        _filterField.KeyDown += OnFilterKeyDown;

        _countLabel = new Label
        {
            X = Pos.AnchorEnd(MinCountWidth + 1),
            Y = 0,
            Width = MinCountWidth,
            TextAlignment = Alignment.End,
        };

        _source = new Source([], _textWidths);
        _tableView = new KeyPassingTableView(theme, _source)
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _tableView.Style.GetOrCreateColumnStyle(ResultColumn).ColorGetter = args => ResultSchemeAt(args.RowIndex);
        _tableView.ValueChanged += (_, _) => RaiseCursorChangedIfMoved();
        _tableView.Accepting += OnTableAccepting;
        _tableView.MouseEvent += OnTableMouse;

        // Added after the table so it draws over the table's empty rows.
        _emptyLabel = new Label { X = Pos.Center(), Y = Pos.Center(), Text = EmptyText, Visible = false };

        Add(_promptLabel, _filterField, _countLabel, _tableView, _emptyLabel);
        ApplyTheme(theme);
        UpdateCountLabel();
    }

    /// <summary>Raised when the cursor moves to another row, or to none when the list empties.</summary>
    public event Action<HistoryRow?>? CursorChanged;

    /// <summary>Raised on Enter or double-click on a row.</summary>
    public event Action<HistoryRow>? RowActivated;

    /// <summary>Raised on a right-click on a row, after the cursor has moved to it, with the click's screen position.</summary>
    public event Action<HistoryRow, Point>? RowMenuRequested;

    public HistoryRow? CurrentRow
    {
        get
        {
            var index = _tableView.Value?.SelectedCell.Y ?? -1;
            var isOnRow = index >= 0 && index < _source.Rows;
            return isOnRow ? _source.Entries[index] : null;
        }
    }

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        _filterField.SetScheme(theme.InputScheme);
        _emptyLabel.SetScheme(theme.DimScheme);
    }

    /// <summary>
    /// Replaces the rows and reapplies the filter, keeping the cursor on the same entry when it is
    /// still listed, or at the same position when it is gone, such as after forgetting it.
    /// </summary>
    public void SetRows(IReadOnlyList<HistoryRow> rows)
    {
        _allRows = rows;
        _isLoaded = true;
        ApplyView();
    }

    public void FocusFilter() => _filterField.SetFocus();

    public void FocusTable() => _tableView.SetFocus();

    /// <summary>
    /// The screen position of the cursor row at <paramref name="column"/> cells from the table's
    /// left edge, scrolling the row into view first; null when the table has no rows.
    /// </summary>
    public Point? CursorRowScreenPosition(int column)
    {
        if (_tableView.Value is not { } selection || CurrentRow is null)
        {
            return null;
        }

        _tableView.EnsureCursorIsVisible();
        var origin = _tableView.ViewportToScreen(Point.Empty);
        var rowOnScreen = KeyPassingTableView.HeaderRows + selection.SelectedCell.Y - _tableView.RowOffset;
        return new Point(origin.X + column, origin.Y + rowOnScreen);
    }

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);

        var width = _tableView.Viewport.Width;
        if (width != _columnLayoutWidth)
        {
            _columnLayoutWidth = width;
            ApplyColumnWidths(width);
        }
    }

    private Scheme? ResultSchemeAt(int index)
    {
        if (index < 0 || index >= _source.Rows)
        {
            return null;
        }

        Color color = _source.Entries[index].Result switch
        {
            HistoryResult.Ok => _theme.Ok,
            HistoryResult.Canceled => _theme.Dim,
            _ => _theme.Error,
        };
        return _theme.CellScheme(color);
    }

    private void ApplyView()
    {
        var keepFile = CurrentRow?.Entry.LogFileName;
        var keepIndex = _tableView.Value?.SelectedCell.Y ?? 0;
        var filter = _filterField.Text;

        var visible = new List<HistoryRow>();
        foreach (var row in _allRows)
        {
            var matches = row.Entry.PackageId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.Entry.Operation.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                visible.Add(row);
            }
        }

        _source = new Source(visible, _textWidths);
        _tableView.Table = _source;

        var cursorIndex = visible.FindIndex(row => row.Entry.LogFileName == keepFile);
        if (cursorIndex < 0)
        {
            cursorIndex = Math.Clamp(keepIndex, 0, Math.Max(0, visible.Count - 1));
        }

        _tableView.Value = new TableSelection(new Point(0, cursorIndex));
        _tableView.EnsureCursorIsVisible();

        UpdateCountLabel();
        _emptyLabel.Visible = _isLoaded && visible.Count == 0;
        RaiseCursorChangedIfMoved();
    }

    private void ApplyColumnWidths(int tableWidth)
    {
        var fixedWidth = 0;
        foreach (var column in Columns)
        {
            fixedWidth += column.Width;
        }

        var fillWidth = Math.Max(2, tableWidth - fixedWidth);
        for (var i = 0; i < Columns.Length; i++)
        {
            var width = Columns[i].Width > 0 ? Columns[i].Width : fillWidth;
            _textWidths[i] = _tableView.SetColumnWidth(i, width);
        }

        _source.SetHeaderWidths(_textWidths);

        // Update, not just a redraw: TableView caches its column layout until told the table changed.
        _tableView.Update();
    }

    private void UpdateCountLabel()
    {
        var noun = _allRows.Count == 1 ? "operation" : "operations";
        var text = _source.Rows == _allRows.Count
            ? $"{_allRows.Count} {noun}"
            : $"{_source.Rows} of {_allRows.Count} {noun}";

        var width = Math.Max(MinCountWidth, DisplayWidth.Of(text));
        _countLabel.X = Pos.AnchorEnd(width + 1);
        _countLabel.Width = width;
        _filterField.Width = Dim.Fill(width + 1);
        _countLabel.Text = text;
    }

    private void OnFilterKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            _filterField.Text = "";
            FocusTable();
            key.Handled = true;
        }
        else if (key == Key.Enter || key == Key.Tab)
        {
            FocusTable();
            key.Handled = true;
        }
    }

    private void OnTableAccepting(object? sender, CommandEventArgs args)
    {
        if (CurrentRow is { } row)
        {
            RowActivated?.Invoke(row);
        }

        // Handled so the accept does not bubble up to the window.
        args.Handled = true;
    }

    private void OnTableMouse(object? sender, Mouse mouse)
    {
        if (mouse.Position is not { } position)
        {
            return;
        }

        if (mouse.IsWheel)
        {
            var step = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? WheelStep : -WheelStep;
            _tableView.ScrollRows(step);
            mouse.Handled = true;
            return;
        }

        var cell = _tableView.ScreenToCell(position, out int? headerColumn);
        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked))
        {
            mouse.Handled = true;
            if (headerColumn is null && cell is { } rowCell && rowCell.Y >= 0 && rowCell.Y < _source.Rows)
            {
                _tableView.Value = new TableSelection(new Point(0, rowCell.Y));
                RowMenuRequested?.Invoke(_source.Entries[rowCell.Y], mouse.ScreenPosition);
            }

            return;
        }

        // The list has no sort, so a header click does nothing, and must not move the cursor.
        if (headerColumn is not null)
        {
            mouse.Handled = true;
        }
    }

    private void RaiseCursorChangedIfMoved()
    {
        var current = CurrentRow;
        if (Equals(current, _lastCursorRow))
        {
            return;
        }

        _lastCursorRow = current;
        CursorChanged?.Invoke(current);
    }

    /// <summary>The rows the filter lets through, as the <see cref="TableView"/> reads them.</summary>
    private sealed class Source : ITableSource
    {
        public Source(IReadOnlyList<HistoryRow> entries, IReadOnlyList<int> headerWidths)
        {
            Entries = entries;
            ColumnNames = new string[HistoryTable.Columns.Length];
            SetHeaderWidths(headerWidths);
        }

        public IReadOnlyList<HistoryRow> Entries { get; }

        public string[] ColumnNames { get; }

        public int Columns => ColumnNames.Length;

        public int Rows => Entries.Count;

        public object this[int row, int col] => HistoryTable.Columns[col].Value(Entries[row]);

        /// <summary>
        /// Pads each header to its column's text width. TableView ignores column minimum widths
        /// while the table has no rows and sizes columns to their headers instead, so padded
        /// headers keep the layout steady when the filter matches nothing.
        /// </summary>
        public void SetHeaderWidths(IReadOnlyList<int> widths)
        {
            for (var i = 0; i < ColumnNames.Length; i++)
            {
                ColumnNames[i] = HistoryTable.Columns[i].Header.PadRight(widths[i]);
            }
        }
    }
}
