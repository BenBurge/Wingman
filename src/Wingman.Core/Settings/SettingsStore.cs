using System.Text;
using System.Text.Json;

namespace Wingman.Core.Settings;

/// <summary>
/// Reads and writes <see cref="WingmanSettings"/> as <c>settings.json</c> in a given directory.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // No BOM: winget and other tools that may read this file expect plain UTF-8 text.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string FilePath { get; }

    public SettingsStore(string directoryPath)
    {
        FilePath = Path.Combine(directoryPath, "settings.json");
    }

    /// <summary>
    /// Creates a store rooted at <c>%APPDATA%\Wingman</c> (or the OS equivalent of
    /// <see cref="Environment.SpecialFolder.ApplicationData"/>).
    /// </summary>
    public static SettingsStore CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wingman");
        return new SettingsStore(directoryPath);
    }

    /// <summary>
    /// Loads settings from disk, or defaults when the file is missing or unreadable. A corrupt
    /// settings file must never stop the TUI from starting, so parse failures fall back to
    /// defaults rather than throwing.
    /// </summary>
    public WingmanSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new WingmanSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WingmanSettings>(json, JsonOptions) ?? new WingmanSettings();
        }
        catch (JsonException)
        {
            return new WingmanSettings();
        }
    }

    public void Save(WingmanSettings settings)
    {
        var directoryPath = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions) + "\n";
        File.WriteAllText(FilePath, json, Utf8NoBom);
    }
}
