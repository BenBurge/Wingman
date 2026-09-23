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
/// text that shows up in package names. Includes the wide emoji-presentation symbols the TUI draws,
/// such as ⚡.
/// </remarks>
public static class DisplayWidth
{
    // Inclusive and in ascending order; IsWide stops at the first range past the code point.
    private static readonly (int First, int Last)[] WideRanges =
    [
        (0x1100, 0x115F),   // Hangul Jamo initial consonants
        (0x231A, 0x231B),   // watch and hourglass
        (0x23E9, 0x23EC),   // fast forward and other media controls
        (0x23F0, 0x23F0),   // alarm clock
        (0x23F3, 0x23F3),   // hourglass with flowing sand
        (0x25FD, 0x25FE),   // white small square and black small square
        (0x2614, 0x2615),   // umbrella and hot beverage
        (0x2648, 0x2653),   // zodiac symbols
        (0x267F, 0x267F),   // wheelchair symbol
        (0x2693, 0x2693),   // anchor
        (0x26A1, 0x26A1),   // high voltage
        (0x26AA, 0x26AB),   // white and black circles
        (0x26BD, 0x26BE),   // soccer ball and baseball
        (0x26C4, 0x26C5),   // snowman and sun with small cloud
        (0x26CE, 0x26CE),   // ophiuchus
        (0x26D4, 0x26D4),   // no entry
        (0x26EA, 0x26EA),   // church
        (0x26F2, 0x26F3),   // fountain and mountain
        (0x26F5, 0x26F5),   // sailboat
        (0x26FA, 0x26FA),   // tent
        (0x26FD, 0x26FD),   // fuel pump
        (0x2705, 0x2705),   // check mark button
        (0x270A, 0x270B),   // fist and raised hand
        (0x2728, 0x2728),   // sparkles
        (0x274C, 0x274C),   // cross mark
        (0x274E, 0x274E),   // negative squared cross mark
        (0x2753, 0x2755),   // question mark and exclamation mark ornament
        (0x2757, 0x2757),   // heavy exclamation mark ornament
        (0x2795, 0x2797),   // plus, minus, division sign
        (0x27B0, 0x27B0),   // curly loop
        (0x27BF, 0x27BF),   // double curly loop
        (0x2B1B, 0x2B1C),   // black and white large squares
        (0x2B50, 0x2B50),   // star
        (0x2B55, 0x2B55),   // heavy large circle
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
