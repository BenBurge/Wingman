using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;

namespace Wingman.Tui;

/// <summary>
/// A filterable, sortable list of packages: a text box and count on the first row, a blank row,
/// then a table with a header row and a background-on-accent cursor row, and an optional footer
/// line. Every list tab uses it; Discover turns the filter box into a search box.
/// </summary>
internal sealed class PackageTable : View
{
    private const int MarkerWidth = 2;
    private const int CountWidth = 14;
    private const int WheelStep = 3;

    // The header is a single row because the header overline and underline are turned off.
    private const int HeaderRows = 1;

    // A blank row and the footer line itself.
    private const int FooterRows = 2;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly IReadOnlyList<PackageColumn> _columns;
    private readonly Label _promptLabel;
    private readonly TextField _filterField;
    private readonly Label _countLabel;
    private readonly TableView _tableView;
    private readonly Label _emptyLabel;
    private readonly Label _footerLabel;

    // Text width of each table column, marker first, excluding the gap TableView draws after it.
    private readonly int[] _textWidths;

    private IReadOnlyList<PackageRow> _allRows = [];
    private PackageTableSource _source;
    private int? _sortColumn;
    private bool _sortDescending;
    private int _columnLayoutWidth = -1;
    private PackageRow? _lastCursorRow;
    private string _prompt = "Filter:";
    private string? _countText;
    private string? _emptyText;
    private string? _footer;

    private bool _isLoading;
    private object? _spinnerTimer;
    private int _spinnerFrame;

    public PackageTable(Theme theme, IReadOnlyList<PackageColumn> columns)
    {
        _columns = columns;
        CanFocus = true;

        _promptLabel = new Label { X = 0, Y = 0, Text = $" {_prompt} " };

        _filterField = new TextField
        {
            X = Pos.Right(_promptLabel),
            Y = 0,
            Width = Dim.Fill(CountWidth + 1),
        };
        _filterField.SetScheme(theme.InputScheme);
        _filterField.TextChanged += (_, _) => OnFilterTextChanged();
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
        _tableView = new KeyPassingTableView(_source)
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
        style.GetOrCreateColumnStyle(0).ColorGetter = _ => MarkerScheme;

        _tableView.ValueChanged += (_, _) => RaiseCursorChangedIfMoved();
        _tableView.Accepting += OnTableAccepting;
        _tableView.MouseEvent += OnTableMouse;
        _tableView.KeyDownNotHandled += OnTableKeyDownNotHandled;

        // Added after the table so it draws over the table's empty rows.
        _emptyLabel = new Label { X = Pos.Center(), Y = Pos.Center(), Visible = false };
        _emptyLabel.SetScheme(theme.DimScheme);

        _footerLabel = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Visible = false };
        _footerLabel.SetScheme(theme.DimScheme);

        Add(_promptLabel, _filterField, _countLabel, _tableView, _emptyLabel, _footerLabel);
        UpdateCountLabel();
    }

    /// <summary>Raised when the cursor moves to another row, or to none when the list empties.</summary>
    public event Action<PackageRow?>? CursorChanged;

    /// <summary>Raised on Enter or double-click on a row.</summary>
    public event Action<PackageRow>? RowActivated;

    /// <summary>Raised with the box's text when Enter is pressed in it while <see cref="FiltersRows"/> is false.</summary>
    public event Action<string>? QuerySubmitted;

    /// <summary>Text for the leading marker column, such as <c>✓</c>; the column stays blank when null.</summary>
    public Func<PackageRow, string>? Marker { get; set; }

    /// <summary>Colors of the marker column; the row's own colors when null.</summary>
    public Scheme? MarkerScheme { get; set; }

    /// <summary>The label before the text box, such as <c>Filter:</c> or <c>Search:</c>.</summary>
    public string Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value;
            _promptLabel.Text = $" {value} ";
        }
    }

    /// <summary>
    /// True: typing in the box filters the rows as you type. False: the box is a search box that
    /// leaves the rows alone, and Enter raises <see cref="QuerySubmitted"/>.
    /// </summary>
    public bool FiltersRows { get; set; } = true;

    /// <summary>What the count label shows when not loading; <c>12 of 142</c> when null.</summary>
    public string? CountText
    {
        get => _countText;
        set
        {
            _countText = value;
            UpdateCountLabel();
        }
    }

    /// <summary>A dim line centered over the table while it has no rows and is not loading; nothing when null.</summary>
    public string? EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            _emptyLabel.Text = value ?? "";
            UpdateEmptyLabel();
        }
    }

    /// <summary>A dim line below the table, after a blank row. Null gives the table those rows back; empty keeps them blank.</summary>
    public string? Footer
    {
        get => _footer;
        set
        {
            _footer = value;
            _footerLabel.Text = value ?? "";
            _footerLabel.Visible = value is not null;
            _tableView.Height = value is null ? Dim.Fill() : Dim.Fill(FooterRows);
        }
    }

    public PackageRow? CurrentRow
    {
        get
        {
            var index = _tableView.Value?.SelectedCell.Y ?? -1;
            var isOnRow = index >= 0 && index < _source.Packages.Count;
            return isOnRow ? _source.Packages[index] : null;
        }
    }

    /// <summary>The text box's contents: the filter, matched against each row's Name and Id, or the search query.</summary>
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
            UpdateEmptyLabel();
        }
    }

    /// <summary>Replaces the data and reapplies the filter and sort, keeping the cursor on the same Id when it is still listed.</summary>
    public void SetRows(IReadOnlyList<PackageRow> rows)
    {
        _allRows = rows;
        ApplyView();
    }

    /// <summary>Redraws the rows so <see cref="Marker"/> is asked again, for when what it reads has changed.</summary>
    public void RefreshMarkers() => _tableView.SetNeedsDraw();

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

    private void OnFilterTextChanged()
    {
        if (FiltersRows)
        {
            ApplyView();
        }
    }

    private void ApplyView()
    {
        var keepId = CurrentRow?.Id;
        var filter = FiltersRows ? _filterField.Text : "";

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
        UpdateEmptyLabel();
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
        style.RepresentationGetter = value => CellText.Fit(value?.ToString() ?? "", textWidth);
    }

    private void OnFilterKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            // A search box keeps its query so the results stay explained.
            if (FiltersRows)
            {
                _filterField.Text = "";
            }

            FocusTable();
            key.Handled = true;
        }
        else if (key == Key.Enter)
        {
            if (!FiltersRows)
            {
                QuerySubmitted?.Invoke(_filterField.Text);
            }

            FocusTable();
            key.Handled = true;
        }
        else if (key == Key.Tab)
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
        if (_isLoading)
        {
            var activity = FiltersRows ? "Loading" : "Searching";
            _countLabel.Text = $"{SpinnerFrames[_spinnerFrame]} {activity}";
        }
        else
        {
            _countLabel.Text = _countText ?? $"{_source.Rows} of {_allRows.Count}";
        }
    }

    private void UpdateEmptyLabel()
    {
        _emptyLabel.Visible = !string.IsNullOrEmpty(_emptyText) && _source.Rows == 0 && !_isLoading;
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

    /// <summary>
    /// A <see cref="TableView"/> that lets printable keys go up to the window while it has no rows.
    /// TableView swallows them then, as type-to-search with nothing to search, which would leave the
    /// tab keys, the key bar, and <c>q</c> dead on an empty list.
    /// </summary>
    private sealed class KeyPassingTableView(ITableSource source) : TableView(source)
    {
        protected override bool OnKeyDownNotHandled(Key key) => Table is { Rows: > 0 } && base.OnKeyDownNotHandled(key);
    }
}
