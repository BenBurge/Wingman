using System.Text.RegularExpressions;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Parses the output of <c>winget show</c> into <see cref="PackageDetails"/>.
/// </summary>
/// <remarks>
/// winget prints <c>Found &lt;Name&gt; [&lt;Id&gt;]</c> and then one <c>Key: Value</c> line per field.
/// Lines indented by two spaces belong to the key above them. Under <c>Tags:</c> each is one tag;
/// under <c>Installer:</c> each is a nested <c>Key: Value</c>. Under any other key they continue a
/// multi-line value such as <c>Description:</c> or <c>Release Notes:</c>, and blank lines between
/// them are part of that value.
/// </remarks>
public static partial class WingetShowParser
{
    private const string Indent = "  ";

    private static readonly HashSet<string> MappedKeys =
    [
        "Version",
        "Publisher",
        "Publisher Url",
        "Author",
        "Moniker",
        "Description",
        "Homepage",
        "License",
        "License Url",
        "Privacy Url",
        "Release Notes",
        "Release Notes Url",
        "Installer.Installer Type",
        "Installer.Installer Url",
        "Installer.Installer SHA256",
        "Installer.Installer Locale",
        "Installer.Installer Size",
    ];

    /// <summary>
    /// Returns the package's details, or null when the output holds no <c>Found ... [...]</c> line,
    /// which is what winget prints when nothing matches.
    /// </summary>
    public static PackageDetails? Parse(string output)
    {
        var lines = SplitLines(output);

        var foundIndex = -1;
        var found = Match.Empty;
        for (var i = 0; i < lines.Length; i++)
        {
            found =FoundLineRegex().Match(lines[i]);
            if (found.Success)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex < 0)
        {
            return null;
        }

        var fields = new Dictionary<string, string>();
        var tags = new List<string>();
        foreach (var entry in ReadEntries(lines[(foundIndex + 1)..]))
        {
            if (entry.Key == "Tags")
            {
                foreach (var line in entry.Continuation)
                {
                    var tag = line.Trim();
                    if (tag.Length > 0)
                    {
                        tags.Add(tag);
                    }
                }
            }
            else if (entry.Key == "Installer")
            {
                foreach (var nested in ReadEntries(entry.Continuation))
                {
                    fields[$"Installer.{nested.Key}"] = JoinValue(nested);
                }
            }
            else
            {
                fields[entry.Key] = JoinValue(entry);
            }
        }

        var additionalFields = new Dictionary<string, string>();
        foreach (var (key, value) in fields)
        {
            if (!MappedKeys.Contains(key))
            {
                additionalFields[key] = value;
            }
        }

        return new PackageDetails
        {
            Id = found.Groups["id"].Value,
            Name = found.Groups["name"].Value,
            Version = fields.GetValueOrDefault("Version", ""),
            Publisher = fields.GetValueOrDefault("Publisher", ""),
            PublisherUrl = fields.GetValueOrDefault("Publisher Url", ""),
            Author = fields.GetValueOrDefault("Author", ""),
            Moniker = fields.GetValueOrDefault("Moniker", ""),
            Description = fields.GetValueOrDefault("Description", ""),
            Homepage = fields.GetValueOrDefault("Homepage", ""),
            License = fields.GetValueOrDefault("License", ""),
            LicenseUrl = fields.GetValueOrDefault("License Url", ""),
            PrivacyUrl = fields.GetValueOrDefault("Privacy Url", ""),
            ReleaseNotes = fields.GetValueOrDefault("Release Notes", ""),
            ReleaseNotesUrl = fields.GetValueOrDefault("Release Notes Url", ""),
            Tags = tags,
            InstallerType = fields.GetValueOrDefault("Installer.Installer Type", ""),
            InstallerUrl = fields.GetValueOrDefault("Installer.Installer Url", ""),
            InstallerSha256 = fields.GetValueOrDefault("Installer.Installer SHA256", ""),
            InstallerLocale = fields.GetValueOrDefault("Installer.Installer Locale", ""),
            InstallerSize = fields.GetValueOrDefault("Installer.Installer Size", ""),
            AdditionalFields = additionalFields,
        };
    }

    private static string[] SplitLines(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        return lines;
    }

    /// <summary>
    /// Reads the unindented <c>Key: Value</c> lines of <paramref name="lines"/>, each with the
    /// indented lines under it, indent removed. Unindented lines without a colon are skipped.
    /// </summary>
    private static List<Entry> ReadEntries(IReadOnlyList<string> lines)
    {
        var entries = new List<Entry>();
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            var colonIndex = line.IndexOf(':');
            if (line.StartsWith(Indent) || colonIndex < 0)
            {
                i++;
                continue;
            }

            // Blank lines belong to the entry only when more indented lines follow them.
            var end = i + 1;
            var next = i + 1;
            while (next < lines.Count && (lines[next].StartsWith(Indent) || lines[next].Length == 0))
            {
                if (lines[next].Length > 0)
                {
                    end = next + 1;
                }

                next++;
            }

            var continuation = new List<string>();
            for (var j = i + 1; j < end; j++)
            {
                var isBlank = lines[j].Length == 0;
                continuation.Add(isBlank ? "" : lines[j][Indent.Length..]);
            }

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();
            entries.Add(new Entry(key, value, continuation));
            i = end;
        }

        return entries;
    }

    private static string JoinValue(Entry entry)
    {
        var parts = new List<string>();
        if (entry.Value.Length > 0)
        {
            parts.Add(entry.Value);
        }

        parts.AddRange(entry.Continuation);
        return string.Join("\n", parts);
    }

    private sealed record Entry(string Key, string Value, IReadOnlyList<string> Continuation);

    [GeneratedRegex(@"^Found (?<name>.+) \[(?<id>[^\]]+)\]$")]
    private static partial Regex FoundLineRegex();
}
