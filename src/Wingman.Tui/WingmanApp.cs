using Terminal.Gui.App;
using Wingman.Core.Elevation;
using Wingman.Core.History;
using Wingman.Core.Operations;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;

namespace Wingman.Tui;

/// <summary>Entry point for the terminal UI.</summary>
public static class WingmanApp
{
    /// <summary>
    /// Names a directory to keep <c>settings.json</c>, <c>package-options.json</c>, and the
    /// <c>history</c> folder in instead of <c>%APPDATA%\Wingman</c>, so <c>tools/TuiHarness</c>
    /// never reads or writes the real profile.
    /// </summary>
    internal const string DataDirectoryVariable = "WINGMAN_DATA_DIR";

    /// <summary>Runs the TUI with every operation in-process, never starting the elevated helper.</summary>
    public static void Run(IWingetClient client) => Run(client, elevation: null);

    /// <param name="elevation">Starts the elevated helper for a batch, prompting for UAC; null runs
    /// every operation in-process.</param>
    /// <param name="themeDetector">What the <c>Auto</c> theme asks for the system's light or dark
    /// mode; null leaves <c>Auto</c> on the dark theme.</param>
    /// <param name="processIsElevated">This process already runs as administrator, so batches never
    /// start the helper and the title bar says so.</param>
    /// <param name="restartAsAdministrator">Starts Wingman again elevated with the given arguments,
    /// returning false when the prompt was declined; null where that is not possible.</param>
    public static void Run(
        IWingetClient client,
        Func<CancellationToken, Task<IElevatedOperationChannel>>? elevation,
        IThemeDetector? themeDetector = null,
        bool processIsElevated = false,
        Func<IReadOnlyList<string>, bool>? restartAsAdministrator = null)
    {
        var settingsStore = CreateSettingsStore();
        var settings = settingsStore.Load();
        var detector = themeDetector ?? new DefaultThemeDetector();
        var theme = Theme.ByName(settings.Theme, detector);

        using var app = Application.Create();
        app.Init();

        var shell = CreateShell(
            app, theme, client, settingsStore, settings, detector, elevation, new ProcessRunner(), processIsElevated, restartAsAdministrator);
        app.Run(shell.Window);
        shell.Window.Dispose();
    }

    internal static SettingsStore CreateSettingsStore() =>
        DataDirectoryOverride() is { } directory ? new SettingsStore(directory) : SettingsStore.CreateDefault();

    internal static PackageOptionsStore CreatePackageOptionsStore() =>
        DataDirectoryOverride() is { } directory ? new PackageOptionsStore(directory) : PackageOptionsStore.CreateDefault();

    internal static HistoryStore CreateHistoryStore() =>
        DataDirectoryOverride() is { } directory ? new HistoryStore(Path.Combine(directory, "history")) : HistoryStore.CreateDefault();

    /// <summary>
    /// Builds the window and every tab. <c>tools/TuiHarness</c> calls this too, so it draws exactly
    /// what the app draws; it passes a <paramref name="commandRunner"/> that never starts a process,
    /// since that runner runs each package's pre- and post-commands.
    /// </summary>
    internal static Shell CreateShell(
        IApplication app,
        Theme theme,
        IWingetClient client,
        SettingsStore settingsStore,
        WingmanSettings settings,
        IThemeDetector themeDetector,
        Func<CancellationToken, Task<IElevatedOperationChannel>>? elevation,
        IProcessRunner commandRunner,
        bool processIsElevated = false,
        Func<IReadOnlyList<string>, bool>? restartAsAdministrator = null)
    {
        var history = CreateHistoryStore();
        var batchRunner = new BatchRunner(client, new PrePostCommandRunner(commandRunner), history, elevation);
        var shell = new Shell(
            app, theme, client, settingsStore, settings, themeDetector, batchRunner, history,
            canElevate: elevation is not null, processIsElevated, restartAsAdministrator);
        shell.SetTabs(
        [
            new InstalledTab(shell, client),
            new DiscoverTab(shell, client),
            new UpdatesTab(shell, client),
            new HistoryTab(shell),
            new SettingsTab(shell),
        ]);
        LoadWingetVersion(shell, client);
        shell.ReloadPins();
        return shell;
    }

    /// <summary>The folder <see cref="DataDirectoryVariable"/> names, or null when it is unset.</summary>
    internal static string? DataDirectoryOverride()
    {
        var directory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    private static void LoadWingetVersion(Shell shell, IWingetClient client)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var version = await client.GetVersionAsync(CancellationToken.None);
                shell.App.Invoke(() => shell.WingetVersion = version);
            }
            catch (Exception ex)
            {
                shell.App.Invoke(() => shell.SetError($"winget: {ex.Message}"));
            }
        });
    }
}
