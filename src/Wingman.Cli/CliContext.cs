using Wingman.Core.History;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Cli;

/// <summary>The client, stores, and output streams a headless command runs against.</summary>
public sealed class CliContext
{
    /// <summary>
    /// Names a directory to keep <c>settings.json</c>, <c>package-options.json</c>, and the
    /// <c>history</c> folder in instead of <c>%APPDATA%\Wingman</c>; the TUI honors the same variable.
    /// </summary>
    public const string DataDirectoryVariable = "WINGMAN_DATA_DIR";

    public required IWingetClient Client { get; init; }

    public required WingmanSettings Settings { get; init; }

    public required SettingsStore SettingsStore { get; init; }

    public required PackageOptionsStore Options { get; init; }

    public required HistoryStore History { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    /// <summary><c>--json</c> was given: commands that support it print one JSON document instead of text.</summary>
    public bool Json { get; init; }

    /// <summary><c>--fake</c> was given: <see cref="Client"/> is a <see cref="FakeWingetClient"/>.</summary>
    public bool IsFake { get; init; }

    /// <summary>
    /// Output goes straight to a terminal, so commands may color it. False when it is redirected,
    /// captured, or <c>NO_COLOR</c> is set.
    /// </summary>
    public bool UseColor { get; init; }

    public required string DataDirectory { get; init; }

    public CancellationToken Cancel { get; init; }

    public static CliContext Create(CliArgs args, TextWriter output, TextWriter error, CancellationToken ct) =>
        Create(args, output, error, ResolveDataDirectory(), ct);

    internal static CliContext Create(
        CliArgs args, TextWriter output, TextWriter error, string dataDirectory, CancellationToken ct)
    {
        var isFake = args.HasFlag("fake");
        IWingetClient client = isFake ? new FakeWingetClient() : new WingetCliClient(new ProcessRunner());
        var settingsStore = new SettingsStore(dataDirectory);

        var writesToConsole = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        var noColorRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        return new CliContext
        {
            Client = client,
            Settings = settingsStore.Load(),
            SettingsStore = settingsStore,
            Options = new PackageOptionsStore(dataDirectory),
            History = new HistoryStore(Path.Combine(dataDirectory, "history")),
            Out = output,
            Error = error,
            Json = args.HasFlag("json"),
            IsFake = isFake,
            UseColor = writesToConsole && !noColorRequested,
            DataDirectory = dataDirectory,
            Cancel = ct,
        };
    }

    /// <summary>The folder <see cref="DataDirectoryVariable"/> names, else <c>%APPDATA%\Wingman</c>.</summary>
    public static string ResolveDataDirectory()
    {
        var directory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            return directory;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wingman");
    }
}
