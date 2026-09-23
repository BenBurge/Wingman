using System.Text;
using Wingman.Core.Winget;

namespace Wingman.Cli;

/// <summary>
/// Writes rows as a fixed-width table under a header, two spaces between columns, the way winget
/// prints its own. Columns take the width of their widest cell until the row would overflow the
/// given width; then the widest columns shrink and their cells end in <c>…</c>. When any row has a
/// marker, such as <c>⊘</c> for a held package, the markers get a first column of their own.
/// </summary>
internal sealed class TableWriter
{
    private const string Gap = "  ";
    private const string Ellipsis = "…";

    // Shrinking stops here so a narrow console still shows the start of every column.
    private const int MinimumColumnWidth = 6;

    private readonly string[] _headers;
    private readonly List<(string Marker, string[] Cells)> _rows = [];

    public TableWriter(params string[] headers)
    {
        _headers = headers;
    }

    public int RowCount => _rows.Count;

    public void AddRow(params string[] cells) => AddMarkedRow("", cells);

    /// <param name="marker">A glyph for the marker column, or <c>""</c> for none.</param>
    public void AddMarkedRow(string marker, params string[] cells)
    {
        if (cells.Length != _headers.Length)
        {
            throw new ArgumentException($"Expected {_headers.Length} cells, got {cells.Length}.", nameof(cells));
        }

        _rows.Add((marker, cells));
    }

    public void Write(TextWriter writer, int maxWidth)
    {
        var markerWidth = 0;
        foreach (var (marker, _) in _rows)
        {
            markerWidth = Math.Max(markerWidth, DisplayWidth.Of(marker));
        }

        var widths = new int[_headers.Length];
        for (var column = 0; column < _headers.Length; column++)
        {
            widths[column] = DisplayWidth.Of(_headers[column]);
            foreach (var (_, cells) in _rows)
            {
                widths[column] = Math.Max(widths[column], DisplayWidth.Of(cells[column]));
            }
        }

        var markerColumn = markerWidth > 0 ? markerWidth + 1 : 0;
        FitWidths(widths, maxWidth - markerColumn);

        writer.WriteLine(FormatRow("", markerColumn, _headers, widths));
        foreach (var (marker, cells) in _rows)
        {
            writer.WriteLine(FormatRow(marker, markerColumn, cells, widths));
        }
    }

    /// <summary>Takes one cell at a time from the widest column until the row fits or nothing can shrink.</summary>
    private static void FitWidths(int[] widths, int available)
    {
        var total = widths.Sum() + (Gap.Length * (widths.Length - 1));
        while (total > available)
        {
            var widest = -1;
            for (var column = 0; column < widths.Length; column++)
            {
                var canShrink = widths[column] > MinimumColumnWidth;
                if (canShrink && (widest < 0 || widths[column] > widths[widest]))
                {
                    widest = column;
                }
            }

            if (widest < 0)
            {
                return;
            }

            widths[widest]--;
            total--;
        }
    }

    private static string FormatRow(string marker, int markerColumn, string[] cells, int[] widths)
    {
        var line = new StringBuilder();
        if (markerColumn > 0)
        {
            line.Append(Pad(marker, markerColumn));
        }

        for (var column = 0; column < cells.Length; column++)
        {
            if (column > 0)
            {
                line.Append(Gap);
            }

            line.Append(Pad(Fit(cells[column], widths[column]), widths[column]));
        }

        return line.ToString().TrimEnd();
    }

    /// <summary><paramref name="text"/> cut to <paramref name="width"/> cells, ending in <c>…</c> when it was cut.</summary>
    internal static string Fit(string text, int width)
    {
        if (DisplayWidth.Of(text) <= width)
        {
            return text;
        }

        var room = width - DisplayWidth.Of(Ellipsis);
        var kept = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = DisplayWidth.Of(rune);
            if (used + runeWidth > room)
            {
                break;
            }

            kept.Append(rune.ToString());
            used += runeWidth;
        }

        return kept + Ellipsis;
    }

    private static string Pad(string text, int width)
    {
        var padding = Math.Max(0, width - DisplayWidth.Of(text));
        return text + new string(' ', padding);
    }
}
