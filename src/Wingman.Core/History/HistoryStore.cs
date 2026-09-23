using System.Globalization;
using System.Text;
using System.Text.Json;
using Wingman.Core.Models;

namespace Wingman.Core.History;

/// <summary>
/// Reads and writes one <c>.json</c>/<c>.log</c> file pair per operation in a history directory.
/// </summary>
public sealed class HistoryStore
{
    private const int DefaultMaxEntries = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // No BOM: consistent with SettingsStore and any tools that may read these files.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string DirectoryPath { get; }

    /// <summary>
    /// The number of entries pruning keeps. Internal so tests can shrink it and exercise
    /// pruning without creating a thousand files.
    /// </summary>
    internal int MaxEntries { get; init; } = DefaultMaxEntries;

    public HistoryStore(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    /// <summary>
    /// Creates a store rooted at <c>%APPDATA%\Wingman\history</c> (or the OS equivalent of
    /// <see cref="Environment.SpecialFolder.ApplicationData"/>).
    /// </summary>
    public static HistoryStore CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wingman", "history");
        return new HistoryStore(directoryPath);
    }

    public HistoryEntry Append(
        DateTimeOffset timestamp,
        string operation,
        string packageId,
        string packageName,
        OperationResult result,
        IReadOnlyList<string> arguments,
        string? batchId)
    {
        Directory.CreateDirectory(DirectoryPath);

        var baseName =
            $"{timestamp.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture)}-{operation}-{Sanitize(packageId)}";
        var (jsonPath, logPath) = ReserveFileNames(baseName);

        var entry = new HistoryEntry(
            timestamp,
            operation,
            packageId,
            packageName,
            result.ExitCode,
            result.Succeeded,
            result.Duration,
            arguments,
            Path.GetFileName(logPath),
            batchId);

        var json = JsonSerializer.Serialize(entry, JsonOptions) + "\n";
        File.WriteAllText(jsonPath, json, Utf8NoBom);

        var log = string.Join("\n", result.Log) + "\n";
        File.WriteAllText(logPath, log, Utf8NoBom);

        Prune();

        return entry;
    }

    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return Array.Empty<HistoryEntry>();
        }

        var entries = new List<HistoryEntry>();
        foreach (var jsonPath in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            var entry = TryRead(jsonPath);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries;
    }

    public string ReadLog(HistoryEntry entry)
    {
        var logPath = Path.Combine(DirectoryPath, entry.LogFileName);
        return File.Exists(logPath) ? File.ReadAllText(logPath) : "";
    }

    public void Delete(HistoryEntry entry)
    {
        var logPath = Path.Combine(DirectoryPath, entry.LogFileName);
        var jsonPath = Path.ChangeExtension(logPath, ".json");

        if (File.Exists(jsonPath))
        {
            File.Delete(jsonPath);
        }

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }

    private void Prune()
    {
        var entries = List();
        if (entries.Count <= MaxEntries)
        {
            return;
        }

        foreach (var stale in entries.Skip(MaxEntries))
        {
            Delete(stale);
        }
    }

    private (string JsonPath, string LogPath) ReserveFileNames(string baseName)
    {
        var candidate = baseName;
        var suffix = 1;
        while (true)
        {
            var jsonPath = Path.Combine(DirectoryPath, candidate + ".json");
            var logPath = Path.Combine(DirectoryPath, candidate + ".log");
            if (!File.Exists(jsonPath) && !File.Exists(logPath))
            {
                return (jsonPath, logPath);
            }

            suffix++;
            candidate = $"{baseName}-{suffix}";
        }
    }

    private static HistoryEntry? TryRead(string jsonPath)
    {
        try
        {
            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<HistoryEntry>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // File names cross an elevated named pipe and end up on disk next to winget's own files, so
    // ids like ARP\Machine\X64\{5EC2AC66-...} need every path-unsafe character replaced. The
    // entry itself keeps the raw id; only the file name is sanitized.
    private static string Sanitize(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        }

        return builder.ToString();
    }
}
