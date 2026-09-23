using Wingman.Core.Elevation;
using Wingman.Core.History;
using Wingman.Core.Notifications;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.State;
using Wingman.Core.Winget;

namespace Wingman.Cli;

/// <summary>The client, stores, and output streams a headless command runs against.</summary>
public sealed class CliContext
{
    /// <summary>
    /// Names a directory to keep <c>settings.json</c>, <c>package-options.json</c>, <c>state.json</c>,
    /// and the <c>history</c> folder in instead of <c>%APPDATA%\Wingman</c>; the TUI honors the same variable.
    /// </summary>
    public const string DataDirectoryVariable = "WINGMAN_DATA_DIR";

    /// <summary>The width tables fit when the console's own width is unknown, as when output is redirected.</summary>
    public const int DefaultWidth = 120;

    public required IWingetClient Client { get; init; }

    public required WingmanSettings Settings { get; init; }

    public required SettingsStore SettingsStore { get; init; }

    public required PackageOptionsStore Options { get; init; }

    public required HistoryStore History { get; init; }

    /// <summary><c>state.json</c>, which <c>check</c> and batches keep current for the tray icon.</summary>
    public required StateStore State { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    /// <summary>Where <c>Proceed? [y/N]</c> reads its answer.</summary>
    public TextReader Input { get; init; } = TextReader.Null;

    /// <summary>
    /// Standard input is not a terminal, so a command cannot ask for confirmation and requires
    /// <c>--yes</c> instead. True unless <see cref="Create(CliArgs, TextWriter, TextWriter, CancellationToken)"/>
    /// finds an interactive console.
    /// </summary>
    public bool IsInputRedirected { get; init; } = true;

    /// <summary><c>--json</c> was given: commands that support it print one JSON document instead of text.</summary>
    public bool Json { get; init; }

    /// <summary><c>--notify</c> was given: <c>check</c> and <c>upgrade</c> hand a toast to <see cref="Notifier"/>.</summary>
    public bool Notify { get; init; }

    /// <summary><c>--fake</c> was given: <see cref="Client"/> is a <see cref="FakeWingetClient"/>.</summary>
    public bool IsFake { get; init; }

    /// <summary>
    /// Output goes straight to a terminal, so commands may color it. False when it is redirected,
    /// captured, or <c>NO_COLOR</c> is set.
    /// </summary>
    public bool UseColor { get; init; }

    /// <summary>The number of columns a table row may take.</summary>
    public int Width { get; init; } = DefaultWidth;

    /// <summary>
    /// Opens the elevated helper for a batch, prompting for UAC; null runs every operation in-process,
    /// as off Windows and with the fake client. The host sets it on Windows.
    /// </summary>
    public Func<CancellationToken, Task<IElevatedOperationChannel>>? ElevationFactory { get; init; }

    /// <summary>This process already runs as administrator, so batches never start the helper.</summary>
    public bool ProcessIsElevated { get; init; }

    /// <summary>Shows a toast; null where there is no way to show one. The host sets it on Windows.</summary>
    public Action<ToastContent>? Notifier { get; init; }

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
            State = new StateStore(dataDirectory),
            Out = output,
            Error = error,
            Input = Console.In,
            IsInputRedirected = Console.IsInputRedirected,
            Json = args.HasFlag("json"),
            Notify = args.HasFlag("notify"),
            IsFake = isFake,
            UseColor = writesToConsole && !noColorRequested,
            Width = writesToConsole ? ConsoleWidth() : DefaultWidth,
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

    // WindowWidth throws when there is no console window, as under some CI runners. A row that
    // fills the last column makes some consoles wrap and print a blank line, so tables stop one short.
    private static int ConsoleWidth()
    {
        try
        {
            var width = Console.WindowWidth;
            return width > 1 ? width - 1 : DefaultWidth;
        }
        catch (IOException)
        {
            return DefaultWidth;
        }
    }
}
