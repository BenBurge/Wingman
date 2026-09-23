namespace Wingman.Core.Winget;

/// <summary>
/// Parses the output of <c>winget show --versions</c>: a <c>Version</c> header, a dash line, and
/// one version per line, newest first.
/// </summary>
public static class WingetVersionsParser
{
    /// <summary>
    /// Returns the versions in the order winget printed them, or an empty list when the output has
    /// no <c>Version</c> table.
    /// </summary>
    public static IReadOnlyList<string> Parse(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        var versions = new List<string>();
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            var isHeader = lines[i].Trim() == "Version" && IsDashLine(lines[i + 1]);
            if (!isHeader)
            {
                continue;
            }

            for (var j = i + 2; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                versions.Add(lines[j].Trim());
            }

            break;
        }

        return versions;
    }

    private static bool IsDashLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && trimmed.Trim('-').Length == 0;
    }
}
