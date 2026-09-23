using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Wingman.Core.Models;
using Wingman.Core.Winget;
using Color = Terminal.Gui.Drawing.Color;

namespace Wingman.Tui;

/// <summary>
/// A filterable, sortable list of packages: a text box and count on the first row, a blank row,
/// then a table with a header row and a background-on-accent cursor row, and an optional footer
/// line. Every list tab uses it; Discover turns the filter box into a search box. The marker
/// column holds <c>●</c> for a row marked for the batch, then the tab's own marker.
/// </summary>
internal sealed class PackageTable : View, IThemedView
{
    private const string MarkedGlyph = "●";

    // The marked glyph, the tab's own marker, and the gap after them.
    private const int MarkerWidth = 3;

    // The narrowest the count label gets, so the filter box does not change width as a spinner or short count comes and goes.
    private const int MinCountWidth = 14;
    private const int WheelStep = 3;

    // A blank row and the footer line itself.
    private const int FooterRows = 2;

    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly IReadOnlyList<PackageColumn> _columns;
    private readonly Label _promptLabel;
    private readonly TextField _filterField;
    private readonly Label _countLabel;
    private readonly KeyPassingTableView _tableView;
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
    private int _countWidth = MinCountWidth;

    private bool _isLoading;
    private object? _spinnerTimer;
    private int _spinnerFrame;
    private Theme _theme;

    public PackageTable(Theme theme, IReadOnlyList<PackageColumn> columns)
    {
        _theme = theme;
        _columns = columns;
        CanFocus = true;

        _promptLabel = new Label { X = 0, Y = 0, Text = $" {_prompt} " };

        _filterField = new TextField
        {
            X = Pos.Right(_promptLabel),
            Y = 0,
            Width = Dim.Fill(MinCountWidth + 1),
        };
        _filterField.TextChanged += (_, _) => OnFilterTextChanged();
        _filterField.KeyDown += OnFilterKeyDown;

        _countLabel = new Label
        {
            X = Pos.AnchorEnd(MinCountWidth + 1),
            Y = 0,
            Width = MinCountWidth,
            TextAlignment = Alignment.End,
        };

        _textWidths = new int[columns.Count + 1];
        _source = new PackageTableSource(columns, [], MarkerText, null, false, _textWidths);
        _tableView = new KeyPassingTableView(theme, _source)
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        var style = _tableView.Style;
        style.GetOrCreateColumnStyle(0).ColorGetter = args => MarkerSchemeAt(args.RowIndex);
        style.RowColorGetter = args => RowSchemeAt(args.RowIndex);

        _tableView.ValueChanged += (_, _) => RaiseCursorChangedIfMoved();
        _tableView.Accepting += OnTableAccepting;
        _tableView.MouseEvent += OnTableMouse;

        // Added after the table so it draws over the table's empty rows.
        _emptyLabel = new Label { X = Pos.Center(), Y = Pos.Center(), Visible = false };

        _footerLabel = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Visible = false };

        Add(_promptLabel, _filterField, _countLabel, _tableView, _emptyLabel, _footerLabel);
        ApplyTheme(theme);
        UpdateCountLabel();
    }

    /// <summary>Raised when the cursor moves to another row, or to none when the list empties.</summary>
    public event Action<PackageRow?>? CursorChanged;

    /// <summary>Raised on Enter or double-click on a row.</summary>
    public event Action<PackageRow>? RowActivated;

    /// <summary>Raised on a right-click on a row, after the cursor has moved to it, with the click's screen position.</summary>
    public event Action<PackageRow, Point>? RowMenuRequested;

    /// <summary>Raised with the box's text when Enter is pressed in it while <see cref="FiltersRows"/> is false.</summary>
    public event Action<string>? QuerySubmitted;

    /// <summary>The tab's own marker, such as <c>✓</c>, drawn after the marked glyph's cell; none when null.</summary>
    public Func<PackageRow, string>? Marker { get; set; }

    /// <summary>The marker column's text color in a theme; the row's own colors when null.</summary>
    public Func<Theme, Color>? MarkerColor { get; set; }

    /// <summary>Whether a row is marked for the batch, which puts <c>●</c> first in its marker column; no row is when null.</summary>
    public Func<PackageRow, bool>? IsMarked { get; set; }

    /// <summary>A marked row's marker column text color in a theme, over <see cref="RowColor"/> and <see cref="MarkerColor"/>.</summary>
    public Func<Theme, Color>? MarkedColor { get; set; }

    /// <summary>A whole row's text color in a theme, marker included, such as dim for a held package; the usual colors when it returns null.</summary>
    public Func<PackageRow, Theme, Color?>? RowColor { get; set; }

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

    /// <summary>
    /// The count label's text from the visible rows and all rows, when <see cref="CountText"/> is null;
    /// <c>12 of 142</c> when this is null too. Call <see cref="RefreshCount"/> when what it reads changes.
    /// </summary>
    public Func<IReadOnlyList<PackageRow>, IReadOnlyList<PackageRow>, string>? CountFormat { get; set; }

    /// <summary>What the count label shows when not loading, over <see cref="CountFormat"/>.</summary>
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
            UpdateFooterLabel();
            _footerLabel.Visible = value is not null;
            _tableView.Height = value is null ? Dim.Fill() : Dim.Fill(FooterRows);
        }
    }

    /// <summary>The rows the filter lets through, in display order.</summary>
    public IReadOnlyList<PackageRow> VisibleRows => _source.Packages;

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

    /// <summary>Redraws the rows so <see cref="Marker"/> and <see cref="IsMarked"/> are asked again, for when what they read has changed.</summary>
    public void RefreshMarkers() => _tableView.SetNeedsDraw();

    /// <summary>Asks <see cref="CountFormat"/> again, for when what it reads has changed.</summary>
    public void RefreshCount() => UpdateCountLabel();

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

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        _filterField.SetScheme(theme.InputScheme);
        _emptyLabel.SetScheme(theme.DimScheme);
        _footerLabel.SetScheme(theme.DimScheme);
    }

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
            UpdateFooterLabel();
        }
    }

    /// <summary>Fits the footer to the pane with a trailing <c>…</c>; the label alone would cut it off mid-word.</summary>
    private void UpdateFooterLabel()
    {
        var width = Math.Max(0, _columnLayoutWidth - 2);
        _footerLabel.Text = CellText.Fit(_footer ?? "", width);
    }

    private Scheme? RowSchemeAt(int index)
    {
        var isOnRow = index >= 0 && index < _source.Packages.Count;
        if (!isOnRow || RowColor?.Invoke(_source.Packages[index], _theme) is not { } color)
        {
            return null;
        }

        return _theme.CellScheme(color);
    }

    private Scheme? MarkerSchemeAt(int index)
    {
        var isOnRow = index >= 0 && index < _source.Packages.Count;
        var isMarked = isOnRow && IsMarked is { } isMarkedFor && isMarkedFor(_source.Packages[index]);
        if (isMarked && MarkedColor is { } markedColor)
        {
            return _theme.CellScheme(markedColor(_theme));
        }

        if (RowSchemeAt(index) is { } rowScheme)
        {
            return rowScheme;
        }

        return MarkerColor is { } markerColor ? _theme.CellScheme(markerColor(_theme)) : null;
    }

    /// <summary>
    /// <c>●</c> or a blank, then the tab's marker, such as <c>●✓</c> or <c> ✓</c>, so the tab's
    /// markers stay in one column whether or not a row is marked.
    /// </summary>
    private string MarkerText(PackageRow row)
    {
        var ownMarker = Marker?.Invoke(row) ?? "";
        var isMarked = IsMarked?.Invoke(row) ?? false;
        if (!isMarked && ownMarker.Length == 0)
        {
            return "";
        }

        return (isMarked ? MarkedGlyph : " ") + ownMarker;
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

        _source = new PackageTableSource(_columns, visible, MarkerText, _sortColumn, _sortDescending, _textWidths);
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

    private void SetColumnWidth(int tableColumn, int width) =>
        _textWidths[tableColumn] = _tableView.SetColumnWidth(tableColumn, width);

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
                _tableView.ScrollRows(WheelStep);
            }
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                _tableView.ScrollRows(-WheelStep);
            }

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
                RowMenuRequested?.Invoke(_source.Packages[rowCell.Y], mouse.ScreenPosition);
            }

            return;
        }

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
        string text;
        if (_isLoading)
        {
            var activity = FiltersRows ? "Loading" : "Searching";
            text = $"{SpinnerFrames[_spinnerFrame]} {activity}";
        }
        else if (_countText is not null)
        {
            text = _countText;
        }
        else if (CountFormat is { } format)
        {
            text = format(_source.Packages, _allRows);
        }
        else
        {
            text = $"{_source.Rows} of {_allRows.Count}";
        }

        // The label grows leftward over the filter box for a long count such as "6 available · 3 marked · 1 held".
        var width = Math.Max(MinCountWidth, DisplayWidth.Of(text));
        if (width != _countWidth)
        {
            _countWidth = width;
            _countLabel.X = Pos.AnchorEnd(width + 1);
            _countLabel.Width = width;
            _filterField.Width = Dim.Fill(width + 1);
        }

        _countLabel.Text = text;
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
}
