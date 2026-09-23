using System.Text.RegularExpressions;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Parses the fixed-width table printed by <c>winget pin list</c> into <see cref="Pin"/> values.
/// </summary>
/// <remarks>
/// Works like <see cref="WingetTableParser"/>: column boundaries are the header's offsets and each
/// row is cut at those boundaries measured in terminal cells. Two of the headers are two words
/// (<c>Pin type</c>, <c>Pinned version</c>), so columns are found by those exact phrases rather
/// than by single words. When no pins exist winget prints a sentence instead of a table, which
/// yields an empty list.
/// </remarks>
public static partial class WingetPinListParser
{
    public static IReadOnlyList<Pin> Parse(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var pins = new List<Pin>();
        for (var i = 0; i < lines.Length; i++)
        {
            var columns = ExtractColumns(lines[i]);
            var isHeader = columns.Exists(c => c.Name == "Id") && columns.Exists(c => c.Name == "Pin type");
            if (!isHeader)
            {
                continue;
            }

            // i + 1 is the dash separator line; data starts right after it.
            for (var j = i + 2; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                pins.Add(ParseRow(lines[j], columns));
            }

            break;
        }

        return pins;
    }

    /// <summary>
    /// Returns each known column, left to right, with the display column it starts at. Header text
    /// is ASCII, so char offsets are also display columns.
    /// </summary>
    private static List<(string Name, int Start)> ExtractColumns(string headerLine)
    {
        var columns = new List<(string Name, int Start)>();
        foreach (Match match in HeaderPhraseRegex().Matches(headerLine))
        {
            columns.Add((match.Value, match.Index));
        }

        return columns;
    }

    private static Pin ParseRow(string line, List<(string Name, int Start)> columns)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < columns.Count; i++)
        {
            var (name, start) = columns[i];
            var startIndex = DisplayWidth.CharIndexAtColumn(line, start);
            var endIndex = i + 1 < columns.Count ? DisplayWidth.CharIndexAtColumn(line, columns[i + 1].Start) : line.Length;
            values[name] = line[startIndex..endIndex].Trim();
        }

        return new Pin(
            Id: values.GetValueOrDefault("Id", ""),
            Name: values.GetValueOrDefault("Name", ""),
            Version: values.GetValueOrDefault("Version", ""),
            Source: values.GetValueOrDefault("Source", ""),
            PinType: ParsePinType(values.GetValueOrDefault("Pin type", "")),
            PinnedVersion: values.GetValueOrDefault("Pinned version", ""));
    }

    // Pinning is what `winget pin add` creates when given no type, so it is the fallback for text
    // that names none of the three.
    private static PinType ParsePinType(string text) =>
        Enum.TryParse<PinType>(text, ignoreCase: true, out var pinType) ? pinType : PinType.Pinning;

    [GeneratedRegex(@"\b(Name|Id|Version|Source|Pin type|Pinned version)\b")]
    private static partial Regex HeaderPhraseRegex();
}
