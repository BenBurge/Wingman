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
}
