using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wingman.Core.Bundles;

namespace Wingman.Core.Options;

/// <summary>
/// Reads and writes per-package <see cref="InstallOptions"/> and <see cref="UpdatesOptions"/> as
/// <c>package-options.json</c> in a given directory, keyed by winget package id.
/// </summary>
public sealed class PackageOptionsStore
{
    // Top-level keys are camelCase, but the option objects keep UniGetUI's PascalCase property
    // names verbatim so bundles stay interchangeable with UniGetUI; that means no naming policy
    // here, only the explicit JsonPropertyName attributes on PackageOptionsDocument.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    // No BOM: winget and other tools that may read this file expect plain UTF-8 text.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Dictionary<string, InstallOptions> _installOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UpdatesOptions> _updatesOptions = new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }

    public PackageOptionsStore(string directoryPath)
    {
        FilePath = Path.Combine(directoryPath, "package-options.json");
        Load();
    }

    /// <summary>
    /// Creates a store rooted at <c>%APPDATA%\Wingman</c> (or the OS equivalent of
    /// <see cref="Environment.SpecialFolder.ApplicationData"/>).
    /// </summary>
    public static PackageOptionsStore CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wingman");
        return new PackageOptionsStore(directoryPath);
    }

    public InstallOptions GetInstallOptions(string id) =>
        _installOptions.TryGetValue(id, out var options) ? options : new InstallOptions();

    public void SetInstallOptions(string id, InstallOptions options)
    {
        if (options.IsDefault())
        {
            _installOptions.Remove(id);
        }
        else
        {
            _installOptions[id] = options;
        }

        Save();
    }

    public UpdatesOptions GetUpdatesOptions(string id) =>
        _updatesOptions.TryGetValue(id, out var options) ? options : new UpdatesOptions();

    public void SetUpdatesOptions(string id, UpdatesOptions options)
    {
        if (options.IsDefault())
        {
            _updatesOptions.Remove(id);
        }
        else
        {
            _updatesOptions[id] = options;
        }

        Save();
    }

    public IReadOnlyCollection<string> AllIds()
    {
        var ids = new HashSet<string>(_installOptions.Keys, StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(_updatesOptions.Keys);
        return ids;
    }

    public bool HasCustomInstallOptions(string id) => _installOptions.ContainsKey(id);

    // A broken package-options.json must never stop the TUI from starting, so a missing file or a
    // parse failure falls back to empty dictionaries rather than throwing.
    private void Load()
    {
        _installOptions.Clear();
        _updatesOptions.Clear();

        if (!File.Exists(FilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var document = JsonSerializer.Deserialize<PackageOptionsDocument>(json, JsonOptions);
            if (document is null)
            {
                return;
            }

            foreach (var (id, options) in document.InstallOptions)
            {
                _installOptions[id] = options;
            }

            foreach (var (id, options) in document.UpdatesOptions)
            {
                _updatesOptions[id] = options;
            }
        }
        catch (JsonException)
        {
            _installOptions.Clear();
            _updatesOptions.Clear();
        }
    }

    private void Save()
    {
        var directoryPath = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var document = new PackageOptionsDocument
        {
            InstallOptions = _installOptions,
            UpdatesOptions = _updatesOptions,
        };

        var json = JsonSerializer.Serialize(document, JsonOptions) + "\n";

        // Write to a temp file and move it into place so a crash mid-write never leaves a
        // truncated package-options.json behind.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json, Utf8NoBom);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    private sealed class PackageOptionsDocument
    {
        [JsonPropertyName("installOptions")]
        public Dictionary<string, InstallOptions> InstallOptions { get; set; } = [];

        [JsonPropertyName("updatesOptions")]
        public Dictionary<string, UpdatesOptions> UpdatesOptions { get; set; } = [];
    }
}
