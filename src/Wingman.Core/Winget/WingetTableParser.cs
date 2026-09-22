using System.Text.RegularExpressions;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Parses winget's fixed-width table output (as produced by <c>list</c>, <c>search</c>, and
/// <c>upgrade</c>) into <see cref="PackageRow"/> values.
/// </summary>
/// <remarks>
/// Known limitation: winget pads columns by display width, so rows containing East Asian wide
/// characters can shift columns relative to the header. This will be revisited once real fixtures
/// (<see cref="WingetCliClient"/>, captured via <c>tools/Capture-WingetFixtures.ps1</c>) show how
/// often that actually happens.
/// </remarks>
public static partial class WingetTableParser
{
    private static readonly string[] KnownColumns = ["Name", "Id", "Version", "Available", "Source"];

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
            var end = i + 1 < columns.Count ? columns[i + 1].Start : line.Length;
            values[name] = Slice(line, start, end);
        }

        var availableVersion = values.GetValueOrDefault("Available", "");

        return new PackageRow(
            Name: values.GetValueOrDefault("Name", ""),
            Id: values.GetValueOrDefault("Id", ""),
            Version: values.GetValueOrDefault("Version", ""),
            AvailableVersion: values.ContainsKey("Available") && availableVersion.Length > 0 ? availableVersion : null,
            Source: values.GetValueOrDefault("Source", ""));
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var length = Math.Min(end, line.Length) - start;
        return line.Substring(start, length).Trim();
    }

    [GeneratedRegex(@"\b(Name|Id|Version|Available|Source)\b")]
    private static partial Regex HeaderWordRegex();
}
