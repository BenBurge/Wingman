using Terminal.Gui.Views;
using Wingman.Core.Models;

namespace Wingman.Tui;

/// <summary>
/// The rows a <see cref="PackageTable"/> currently shows, already filtered and sorted, as the
/// <see cref="ITableSource"/> its <see cref="TableView"/> reads. Table column 0 is the marker
/// column; column <c>i + 1</c> is <c>columns[i]</c>. A new source is built for every filter or
/// sort change; only the header widths change afterward, on resize.
/// </summary>
internal sealed class PackageTableSource : ITableSource
{
    private readonly IReadOnlyList<PackageColumn> _columns;
    private readonly Func<PackageRow, string>? _marker;
    private readonly int? _sortColumn;
    private readonly bool _sortDescending;

    public PackageTableSource(
        IReadOnlyList<PackageColumn> columns,
        IReadOnlyList<PackageRow> packages,
        Func<PackageRow, string>? marker,
        int? sortColumn,
        bool sortDescending,
        IReadOnlyList<int> headerWidths)
    {
        _columns = columns;
        _marker = marker;
        _sortColumn = sortColumn;
        _sortDescending = sortDescending;
        Packages = packages;
        ColumnNames = new string[columns.Count + 1];
        SetHeaderWidths(headerWidths);
    }

    public IReadOnlyList<PackageRow> Packages { get; }

    public string[] ColumnNames { get; }

    public int Columns => ColumnNames.Length;

    public int Rows => Packages.Count;

    public object this[int row, int col]
    {
        get
        {
            var package = Packages[row];
            if (col == 0)
            {
                return _marker?.Invoke(package) ?? "";
            }

            return _columns[col - 1].Value(package);
        }
    }

    /// <summary>
    /// Pads each header to its column's text width, indexed by table column. TableView ignores
    /// column minimum widths while the table has no rows and sizes columns to their headers
    /// instead, so padded headers keep the layout steady when a filter matches nothing.
    /// </summary>
    public void SetHeaderWidths(IReadOnlyList<int> widths)
    {
        ColumnNames[0] = Pad("", widths, 0);
        for (var i = 0; i < _columns.Count; i++)
        {
            var header = _columns[i].Header;
            if (i == _sortColumn)
            {
                header += _sortDescending ? " ▼" : " ▲";
            }

            ColumnNames[i + 1] = Pad(header, widths, i + 1);
        }
    }

    private static string Pad(string header, IReadOnlyList<int> widths, int tableColumn)
    {
        var width = tableColumn < widths.Count ? widths[tableColumn] : 0;
        return header.PadRight(width);
    }
}
