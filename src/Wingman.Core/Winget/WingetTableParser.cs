using System.Text.RegularExpressions;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Parses winget's fixed-width table output (as produced by <c>list</c>, <c>search</c>, and
/// <c>upgrade</c>) into <see cref="PackageRow"/> values.
/// </summary>
/// <remarks>
/// Column boundaries come from the header line. winget pads every column by display width, so
/// each data row is cut at those boundaries measured in terminal cells (see
/// <see cref="DisplayWidth"/>) rather than in chars; rows holding East Asian wide characters then
/// line up with the header even though they are shorter in chars. The <c>Match</c> column of
/// <c>search</c> output is recognized only as a boundary and its value is not kept. winget
/// truncates long values with an ellipsis only when stdout is a terminal, so redirected output,
/// which is all this parser sees, always carries full values.
/// </remarks>
public static partial class WingetTableParser
{
    private static readonly string[] KnownColumns = ["Name", "Id", "Version", "Match", "Available", "Source"];

    public static IReadOnlyList<PackageRow> Parse(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var headerIndex = Array.FindIndex(lines, IsHeaderLine);
        if (headerIndex < 0)
        {
            return [];
        }

        var columns = ExtractColumns(lines[headerIndex]);

        // headerIndex + 1 is the dash separator line; data starts right after it.
        var rows = new List<PackageRow>();
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || IsTrailingCountLine(line))
            {
                break;
            }

            rows.Add(ParseRow(line, columns));
        }

        return rows;
    }

    private static bool IsHeaderLine(string line) =>
        HeaderWordRegex().Matches(line).Select(m => m.Value).ToHashSet()
            .IsSupersetOf(["Name", "Id", "Version"]);

    private static bool IsTrailingCountLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || !char.IsDigit(trimmed[0]))
        {
            return false;
        }

        return trimmed.Contains(" upgrades available") || trimmed.Contains(" package(s)");
    }

    /// <summary>
    /// Returns each known column with the display column it starts at. Header words are ASCII,
    /// so their char offsets are also their display columns.
    /// </summary>
    private static List<(string Name, int Start)> ExtractColumns(string headerLine)
    {
        var columns = new List<(string Name, int Start)>();
        foreach (Match match in HeaderWordRegex().Matches(headerLine))
        {
            if (Array.IndexOf(KnownColumns, match.Value) >= 0)
            {
                columns.Add((match.Value, match.Index));
            }
        }

        return columns;
    }

    private static PackageRow ParseRow(string line, List<(string Name, int Start)> columns)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < columns.Count; i++)
        {
            var (name, start) = columns[i];
            var startIndex = CharIndexAtDisplayColumn(line, start);
            var endIndex = i + 1 < columns.Count ? CharIndexAtDisplayColumn(line, columns[i + 1].Start) : line.Length;
            values[name] = line[startIndex..endIndex].Trim();
        }

        var availableVersion = values.GetValueOrDefault("Available", "");

        return new PackageRow(
            Name: values.GetValueOrDefault("Name", ""),
            Id: values.GetValueOrDefault("Id", ""),
            Version: values.GetValueOrDefault("Version", ""),
            AvailableVersion: values.ContainsKey("Available") && availableVersion.Length > 0 ? availableVersion : null,
            Source: values.GetValueOrDefault("Source", ""));
    }

    /// <summary>
    /// Returns the char index at which <paramref name="displayColumn"/> begins in
    /// <paramref name="line"/>, or the line's length when the line is narrower than that.
    /// </summary>
    private static int CharIndexAtDisplayColumn(string line, int displayColumn)
    {
        var width = 0;
        var index = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (width >= displayColumn)
            {
                break;
            }

            width += DisplayWidth.Of(rune);
            index += rune.Utf16SequenceLength;
        }

        return index;
    }

    [GeneratedRegex(@"\b(Name|Id|Version|Match|Available|Source)\b")]
    private static partial Regex HeaderWordRegex();
}
