using System.Text;
using Wingman.Core.Winget;

namespace Wingman.Tui;

internal static class CellText
{
    private const string Ellipsis = "…";

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="width"/> terminal cells, ending in <c>…</c>
    /// when it was cut. Widths are measured with <see cref="DisplayWidth"/>, so a wide character
    /// never pushes the text past its cells.
    /// </summary>
    public static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        if (DisplayWidth.Of(text) <= width)
        {
            return text;
        }

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

    /// <summary>
    /// Breaks <paramref name="text"/> into lines of at most <paramref name="width"/> cells, at spaces
    /// where it can and inside a word, such as a long URL, only when the word alone is too wide.
    /// </summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            var used = 0;
            foreach (var word in paragraph.TrimEnd('\r').Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                var wordWidth = DisplayWidth.Of(word);
                var fitsOnLine = used == 0 ? wordWidth <= width : used + 1 + wordWidth <= width;
                if (!fitsOnLine && used > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    used = 0;
                }

                if (used > 0)
                {
                    line.Append(' ');
                    used++;
                }

                foreach (var rune in word.EnumerateRunes())
                {
                    var runeWidth = DisplayWidth.Of(rune);
                    if (used + runeWidth > width)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        used = 0;
                    }

                    line.Append(rune.ToString());
                    used += runeWidth;
                }
            }

            lines.Add(line.ToString());
        }

        return lines;
    }
}
