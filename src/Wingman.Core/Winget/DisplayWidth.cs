using System.Globalization;
using System.Text;

namespace Wingman.Core.Winget;

/// <summary>
/// Measures how many terminal cells text occupies.
/// </summary>
/// <remarks>
/// This mirrors how winget pads its tables: East Asian Wide and Fullwidth characters take two
/// cells, combining marks and format characters take none, and everything else takes one. Only the
/// main Wide and Fullwidth blocks are listed, which covers the CJK, Hangul, fullwidth, and emoji
/// text that shows up in package names.
/// </remarks>
internal static class DisplayWidth
{
    // Inclusive and in ascending order; IsWide stops at the first range past the code point.
    private static readonly (int First, int Last)[] WideRanges =
    [
        (0x1100, 0x115F),   // Hangul Jamo initial consonants
        (0x2E80, 0x303E),   // CJK Radicals through CJK Symbols and Punctuation
        (0x3040, 0xA4CF),   // Hiragana through Yi
        (0xAC00, 0xD7A3),   // Hangul Syllables
        (0xF900, 0xFAFF),   // CJK Compatibility Ideographs
        (0xFE30, 0xFE4F),   // CJK Compatibility Forms
        (0xFF00, 0xFF60),   // Fullwidth Forms
        (0xFFE0, 0xFFE6),   // Fullwidth signs
        (0x1F300, 0x1F64F), // Miscellaneous Symbols and Pictographs, Emoticons
        (0x1F900, 0x1F9FF), // Supplemental Symbols and Pictographs
        (0x20000, 0x2FFFD), // CJK Unified Ideographs Extension B and later
        (0x30000, 0x3FFFD), // CJK Unified Ideographs Extension G and later
    ];

    public static int Of(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        var isZeroWidth = category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format;
        if (isZeroWidth)
        {
            return 0;
        }

        return IsWide(rune.Value) ? 2 : 1;
    }

    public static int Of(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += Of(rune);
        }

        return width;
    }

    /// <summary>
    /// Returns the char index at which <paramref name="displayColumn"/> begins in
    /// <paramref name="line"/>, or the line's length when the line is narrower than that.
    /// </summary>
    public static int CharIndexAtColumn(string line, int displayColumn)
    {
        var width = 0;
        var index = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (width >= displayColumn)
            {
                break;
            }

            width += Of(rune);
            index += rune.Utf16SequenceLength;
        }

        return index;
    }

    private static bool IsWide(int codePoint)
    {
        foreach (var (first, last) in WideRanges)
        {
            if (codePoint < first)
            {
                return false;
            }

            if (codePoint <= last)
            {
                return true;
            }
        }

        return false;
    }
}
