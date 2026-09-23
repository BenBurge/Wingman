using System.Drawing;
using System.Text;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>
/// A filterable, sortable list of packages: a filter box and count on the first row, a blank row,
/// then a table with a header row and a background-on-accent cursor row. Every list tab uses it.
/// </summary>
internal sealed class PackageTable : View
{
    private const int MarkerWidth = 2;
    private const int CountWidth = 14;
    private const int WheelStep = 3;

    // The header is a single row because the header overline and underline are turned off.
    private const int HeaderRows = 1;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly IReadOnlyList<PackageColumn> _columns;
    private readonly TextField _filterField;
    private readonly Label _countLabel;
    private readonly TableView _tableView;

    // Text width of each table column, marker first, excluding the gap TableView draws after it.
    private readonly int[] _textWidths;

    private IReadOnlyList<PackageRow> _allRows = [];
    private PackageTableSource _source;
    private int? _sortColumn;
    private bool _sortDescending;
    private int _columnLayoutWidth = -1;
    private PackageRow? _lastCursorRow;

    private bool _isLoading;
    private object? _spinnerTimer;
    private int _spinnerFrame;

    public PackageTable(Theme theme, IReadOnlyList<PackageColumn> columns)
    {
        _columns = columns;
        CanFocus = true;

        var filterLabel = new Label { X = 0, Y = 0, Text = " Filter: " };

        _filterField = new TextField
        {
            X = Pos.Right(filterLabel),
            Y = 0,
            Width = Dim.Fill(CountWidth + 1),
        };
        _filterField.SetScheme(theme.InputScheme);
        _filterField.TextChanged += (_, _) => ApplyView();
        _filterField.KeyDown += OnFilterKeyDown;

        _countLabel = new Label
        {
            X = Pos.AnchorEnd(CountWidth + 1),
            Y = 0,
            Width = CountWidth,
            TextAlignment = Alignment.End,
        };

        _textWidths = new int[columns.Count + 1];
        _source = new PackageTableSource(columns, [], null, null, false, _textWidths);
        _tableView = new TableView(_source)
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            MultiSelect = false,

            // Type-to-search would swallow the letter keys the key bar dispatches.
            CollectionNavigator = null,
        };
        var style = _tableView.Style;
        style.ShowHorizontalHeaderOverline = false;
        style.ShowHorizontalHeaderUnderline = false;
        style.ShowHorizontalBottomLine = false;
        style.ShowVerticalCellLines = false;
        style.ShowVerticalHeaderLines = false;
        style.InvertSelectedCellFirstCharacter = false;
        style.ExpandLastColumn = true;
        style.AlwaysShowHeaders = true;
        style.HeaderScheme = theme.HeaderScheme;

        _tableView.ValueChanged += (_, _) => RaiseCursorChangedIfMoved();
        _tableView.Accepting += OnTableAccepting;
        _tableView.MouseEvent += OnTableMouse;
        _tableView.KeyDownNotHandled += OnTableKeyDownNotHandled;

        Add(filterLabel, _filterField, _countLabel, _tableView);
        UpdateCountLabel();
    }

    /// <summary>Raised when the cursor moves to another row, or to none when the list empties.</summary>
    public event Action<PackageRow?>? CursorChanged;

    /// <summary>Raised on Enter or double-click on a row.</summary>
    public event Action<PackageRow>? RowActivated;

    /// <summary>Text for the leading marker column, such as <c>✓</c>; the column stays blank when null.</summary>
    public Func<PackageRow, string>? Marker { get; set; }

    public PackageRow? CurrentRow
    {
        get
        {
            var index = _tableView.Value?.SelectedCell.Y ?? -1;
            var isOnRow = index >= 0 && index < _source.Packages.Count;
            return isOnRow ? _source.Packages[index] : null;
        }
    }

    /// <summary>Case-insensitive substring matched against each row's Name and Id.</summary>
    public string Filter
    {
        get => _filterField.Text;
        set => _filterField.Text = value;
    }

    /// <summary>While true the count label shows a spinner instead of the row count.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (value == _isLoading)
            {
                return;
            }

            _isLoading = value;
            if (value && App is { } app)
            {
                _spinnerFrame = 0;
                _spinnerTimer = app.AddTimeout(TimeSpan.FromMilliseconds(100), AdvanceSpinner);
            }
            else if (!value && _spinnerTimer is not null)
            {
                App?.RemoveTimeout(_spinnerTimer);
                _spinnerTimer = null;
            }

            UpdateCountLabel();
        }
    }

    /// <summary>Replaces the data and reapplies the filter and sort, keeping the cursor on the same Id when it is still listed.</summary>
    public void SetRows(IReadOnlyList<PackageRow> rows)
    {
        _allRows = rows;
        ApplyView();
    }

    public void FocusFilter() => _filterField.SetFocus();

    public void FocusTable() => _tableView.SetFocus();

    /// <summary>Sorts by the next column, ascending, wrapping from the last column back to the first.</summary>
    public void CycleSort()
    {
        var isOnLastColumn = _sortColumn is { } current && current == _columns.Count - 1;
        _sortColumn = _sortColumn is null || isOnLastColumn ? 0 : _sortColumn + 1;
        _sortDescending = false;
        ApplyView();
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

    private void ApplyView()
    {
        var keepId = CurrentRow?.Id;
        var filter = _filterField.Text;

        var visible = new List<PackageRow>();
        foreach (var row in _allRows)
        {
            var matches = row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                visible.Add(row);
            }
        }

        if (_sortColumn is { } sortColumn)
        {
            var column = _columns[sortColumn];
            var comparer = column.Comparer ?? StringComparer.OrdinalIgnoreCase;
            visible = _sortDescending
                ? [.. visible.OrderByDescending(column.Value, comparer)]
                : [.. visible.OrderBy(column.Value, comparer)];
        }

        _source = new PackageTableSource(_columns, visible, Marker, _sortColumn, _sortDescending, _textWidths);
        _tableView.Table = _source;

        var cursorIndex = visible.FindIndex(row => string.Equals(row.Id, keepId, StringComparison.OrdinalIgnoreCase));
        _tableView.Value = new TableSelection(new Point(0, Math.Max(cursorIndex, 0)));
        _tableView.EnsureCursorIsVisible();

        UpdateCountLabel();
        RaiseCursorChangedIfMoved();
    }

    /// <summary>
    /// Pins every column to its configured width and splits what is left among the fill columns.
    /// Cell text is truncated here, by display width, so a wide character never pushes a row past
    /// its column.
    /// </summary>
    private void ApplyColumnWidths(int tableWidth)
    {
        var fixedWidth = MarkerWidth;
        var fillCount = 0;
        foreach (var column in _columns)
        {
            if (column.Width > 0)
            {
                fixedWidth += column.Width;
            }
            else
            {
                fillCount++;
            }
        }

        var fillWidth = fillCount == 0 ? 0 : Math.Max(2, (tableWidth - fixedWidth) / fillCount);

        SetColumnWidth(0, MarkerWidth);
        for (var i = 0; i < _columns.Count; i++)
        {
            var width = _columns[i].Width > 0 ? _columns[i].Width : fillWidth;
            SetColumnWidth(i + 1, width);
        }

        _source.SetHeaderWidths(_textWidths);

        // Update, not just a redraw: TableView caches its column layout until told the table changed.
        _tableView.Update();
    }

    private void SetColumnWidth(int tableColumn, int width)
    {
        // TableView draws a one-cell gap after each column's text, and the configured widths include it.
        var textWidth = Math.Max(1, width - 1);
        var style = _tableView.Style.GetOrCreateColumnStyle(tableColumn);
        style.MinWidth = textWidth;
        style.MaxWidth = textWidth;
        _textWidths[tableColumn] = textWidth;
        style.RepresentationGetter = value => Fit(value?.ToString() ?? "", textWidth);
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

    /// <summary>
    /// Keeps the arrow and paging keys the table could not use, at the first or last row, from
    /// bubbling up to the window, where Terminal.Gui would move focus out of the list.
    /// </summary>
    private void OnTableKeyDownNotHandled(object? sender, Key key)
    {
        var isNavigationKey = key == Key.CursorUp || key == Key.CursorDown
            || key == Key.CursorLeft || key == Key.CursorRight
            || key == Key.PageUp || key == Key.PageDown
            || key == Key.Home || key == Key.End;
        if (isNavigationKey)
        {
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
            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                ScrollRows(WheelStep);
            }
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                ScrollRows(-WheelStep);
            }

            mouse.Handled = true;
            return;
        }

        _tableView.ScreenToCell(position, out int? headerColumn);
        if (headerColumn is not { } tableColumn)
        {
            return;
        }

        // Handling the press and release too keeps a header click from moving the cursor.
        mouse.Handled = true;
        if (mouse.IsLeftClick())
        {
            SortByTableColumn(tableColumn);
        }
    }

    private void ScrollRows(int step)
    {
        var visibleRows = Math.Max(1, _tableView.Viewport.Height - HeaderRows);
        var maxOffset = Math.Max(0, _source.Rows - visibleRows);
        _tableView.RowOffset = Math.Clamp(_tableView.RowOffset + step, 0, maxOffset);
        _tableView.SetNeedsDraw();
    }

    private void SortByTableColumn(int tableColumn)
    {
        var isMarkerColumn = tableColumn == 0;
        if (isMarkerColumn)
        {
            return;
        }

        var column = tableColumn - 1;
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        ApplyView();
    }

    private bool AdvanceSpinner()
    {
        if (!_isLoading)
        {
            return false;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
        UpdateCountLabel();
        return true;
    }

    private void UpdateCountLabel()
    {
        _countLabel.Text = _isLoading
            ? $"{SpinnerFrames[_spinnerFrame]} Loading"
            : $"{_source.Rows} of {_allRows.Count}";
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

    private static string Fit(string text, int width)
    {
        if (DisplayWidth.Of(text) <= width)
        {
            return text;
        }

        const string Ellipsis = "…";
        var budget = width - DisplayWidth.Of(Ellipsis);
        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = DisplayWidth.Of(rune);
            if (used + runeWidth > budget)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += runeWidth;
        }

        return builder.Append(Ellipsis).ToString();
    }
}
