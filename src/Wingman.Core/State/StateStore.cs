using System.Text;
using System.Text.Json;

namespace Wingman.Core.State;

/// <summary>
/// Reads and writes <see cref="WingmanState"/> as <c>state.json</c> in a given directory.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // No BOM: winget and other tools that may read this file expect plain UTF-8 text.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string FilePath { get; }

    public StateStore(string directoryPath)
    {
        FilePath = Path.Combine(directoryPath, "state.json");
    }

    /// <summary>
    /// Creates a store rooted at <c>%APPDATA%\Wingman</c> (or the OS equivalent of
    /// <see cref="Environment.SpecialFolder.ApplicationData"/>).
    /// </summary>
    public static StateStore CreateDefault()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wingman");
        return new StateStore(directoryPath);
    }

    /// <summary>When <c>state.json</c> was last written, or <c>null</c> if it has never been saved.</summary>
    public DateTimeOffset? LastWriteTime =>
        File.Exists(FilePath) ? new DateTimeOffset(File.GetLastWriteTimeUtc(FilePath), TimeSpan.Zero) : null;

    /// <summary>
    /// Loads state from disk, or defaults when the file is missing or unreadable. A corrupt state
    /// file must never stop <c>wingman check</c> or the tray from starting, so parse failures fall
    /// back to defaults rather than throwing.
    /// </summary>
    public WingmanState Load()
    {
        if (!File.Exists(FilePath))
        {
            return new WingmanState();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WingmanState>(json, JsonOptions) ?? new WingmanState();
        }
        catch (JsonException)
        {
            return new WingmanState();
        }
    }

    public void Save(WingmanState state)
    {
        var directoryPath = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions) + "\n";

        // Write to a temp file and move it into place so a crash mid-write never leaves a
        // truncated state.json behind for the tray or a concurrent `wingman check` to read.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json, Utf8NoBom);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>Loads the current state, applies <paramref name="change"/>, and saves the result.</summary>
    public void Update(Action<WingmanState> change)
    {
        var state = Load();
        change(state);
        Save(state);
    }
}
