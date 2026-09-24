using System.Text;
using Terminal.Gui.Drawing;
using Wingman.Core.Winget;
using Wingman.Tui;
using Color = Terminal.Gui.Drawing.Color;

namespace TuiHarness;

/// <summary>
/// Renders a frame of screen cells as an SVG for the README: one rect per run of cells sharing a
/// background and one text per run sharing a foreground, inside a rounded window frame.
/// </summary>
internal static class SvgScreenshot
{
    private const int CellWidth = 9;
    private const int CellHeight = 18;
    private const int FramePadding = 8;
    private const int FrameRadius = 12;
    private const string FrameColor = "#0E1117";
    private const int FontSize = 15;
    private const string FontFamily = "Cascadia Mono, Consolas, 'JetBrains Mono', monospace";

    // Where the baseline sits in an 18 px row so a 15 px font's ascenders and descenders both fit.
    private const int BaselineOffset = 14;

    private readonly record struct Glyph(int X, string Text, int Cells, Color Foreground, bool Bold)
    {
        public bool IsSpace => Text == " ";
    }

    public static string Render(Cell[,] cells, Theme theme)
    {
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var gridWidth = columns * CellWidth;
        var gridHeight = rows * CellHeight;
        var width = gridWidth + 2 * FramePadding;
        var height = gridHeight + 2 * FramePadding;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">\n");
        svg.Append($"<rect width=\"{width}\" height=\"{height}\" rx=\"{FrameRadius}\" fill=\"{FrameColor}\"/>\n");
        svg.Append($"<g transform=\"translate({FramePadding} {FramePadding})\">\n");
        svg.Append($"<rect width=\"{gridWidth}\" height=\"{gridHeight}\" fill=\"{Hex(theme.Background)}\"/>\n");
        for (var y = 0; y < rows; y++)
        {
            AppendBackgroundRuns(svg, cells, y, theme.Background);
        }

        svg.Append($"<g font-family=\"{FontFamily}\" font-size=\"{FontSize}\">\n");
        for (var y = 0; y < rows; y++)
        {
            AppendTextRuns(svg, Glyphs(cells, y, theme.Foreground), y);
        }

        svg.Append("</g>\n</g>\n</svg>\n");
        return svg.ToString();
    }

    private static void AppendBackgroundRuns(StringBuilder svg, Cell[,] cells, int y, Color themeBackground)
    {
        var columns = cells.GetLength(1);
        var x = 0;
        while (x < columns)
        {
            var background = cells[y, x].Attribute?.Background ?? themeBackground;
            var start = x;
            while (x < columns && (cells[y, x].Attribute?.Background ?? themeBackground) == background)
            {
                x++;
            }

            // The grid's own rect already paints the theme background.
            if (background != themeBackground)
            {
                svg.Append($"<rect x=\"{start * CellWidth}\" y=\"{y * CellHeight}\" width=\"{(x - start) * CellWidth}\" height=\"{CellHeight}\" fill=\"{Hex(background)}\"/>\n");
            }
        }
    }

    /// <summary>The row's glyphs left to right; a wide glyph covers two cells and the driver's placeholder in the second is dropped.</summary>
    private static List<Glyph> Glyphs(Cell[,] cells, int y, Color themeForeground)
    {
        var columns = cells.GetLength(1);
        var glyphs = new List<Glyph>();
        var x = 0;
        while (x < columns)
        {
            var cell = cells[y, x];
            var text = string.IsNullOrEmpty(cell.Grapheme) ? " " : cell.Grapheme;
            var cellCount = Math.Min(DisplayWidth.Of(text) == 2 ? 2 : 1, columns - x);
            var foreground = cell.Attribute?.Foreground ?? themeForeground;
            var bold = cell.Attribute is { } attribute && attribute.Style.HasFlag(TextStyle.Bold);
            glyphs.Add(new Glyph(x, text, cellCount, foreground, bold));
            x += cellCount;
        }

        return glyphs;
    }

    /// <summary>
    /// Emits one text per run of narrow glyphs sharing a foreground and weight, and one per wide
    /// glyph. Each text's textLength spans exactly its cells, so columns stay aligned whatever the
    /// font's advance or a fallback glyph's width.
    /// </summary>
    private static void AppendTextRuns(StringBuilder svg, List<Glyph> glyphs, int y)
    {
        var start = 0;
        while (start < glyphs.Count)
        {
            var end = start + 1;
            var isWide = glyphs[start].Cells == 2;
            while (!isWide && end < glyphs.Count && ContinuesRun(glyphs[start], glyphs[end]))
            {
                end++;
            }

            AppendText(svg, glyphs, start, end, y);
            start = end;
        }
    }

    private static bool ContinuesRun(Glyph first, Glyph next) =>
        next.Cells == 1 && next.Foreground == first.Foreground && next.Bold == first.Bold;

    // Leading and trailing spaces draw nothing, so they are trimmed and an all-space run is skipped.
    private static void AppendText(StringBuilder svg, List<Glyph> glyphs, int start, int end, int y)
    {
        while (start < end && glyphs[start].IsSpace)
        {
            start++;
        }

        while (end > start && glyphs[end - 1].IsSpace)
        {
            end--;
        }

        if (start == end)
        {
            return;
        }

        var first = glyphs[start];
        var last = glyphs[end - 1];
        var textLength = (last.X + last.Cells - first.X) * CellWidth;
        svg.Append($"<text x=\"{first.X * CellWidth}\" y=\"{y * CellHeight + BaselineOffset}\" fill=\"{Hex(first.Foreground)}\" textLength=\"{textLength}\" xml:space=\"preserve\"");
        if (first.Bold)
        {
            svg.Append(" font-weight=\"bold\"");
        }

        svg.Append('>');
        for (var i = start; i < end; i++)
        {
            AppendEscaped(svg, glyphs[i].Text);
        }

        svg.Append("</text>\n");
    }

    private static void AppendEscaped(StringBuilder svg, string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    svg.Append("&amp;");
                    break;
                case '<':
                    svg.Append("&lt;");
                    break;
                case '>':
                    svg.Append("&gt;");
                    break;
                case < ' ':
                    // Control characters are not allowed in XML text.
                    svg.Append(' ');
                    break;
                default:
                    svg.Append(character);
                    break;
            }
        }
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
