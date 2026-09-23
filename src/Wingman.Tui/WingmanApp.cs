using Terminal.Gui.App;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;

namespace Wingman.Tui;

/// <summary>Entry point for the terminal UI.</summary>
public static class WingmanApp
{
    /// <summary>
    /// Names a directory to keep <c>settings.json</c> and <c>package-options.json</c> in instead of
    /// <c>%APPDATA%\Wingman</c>, so <c>tools/TuiHarness</c> never reads or writes the real profile.
    /// </summary>
    internal const string DataDirectoryVariable = "WINGMAN_DATA_DIR";

    public static void Run(IWingetClient client)
    {
        var settings = CreateSettingsStore().Load();
        var theme = Theme.ByName(settings.Theme);

        using var app = Application.Create();
        app.Init();

        var shell = CreateShell(app, theme, client, settings);
        app.Run(shell.Window);
        shell.Window.Dispose();
    }

    internal static SettingsStore CreateSettingsStore() =>
        DataDirectoryOverride() is { } directory ? new SettingsStore(directory) : SettingsStore.CreateDefault();

    internal static PackageOptionsStore CreatePackageOptionsStore() =>
        DataDirectoryOverride() is { } directory ? new PackageOptionsStore(directory) : PackageOptionsStore.CreateDefault();

    /// <summary>Builds the window and every tab. <c>tools/TuiHarness</c> calls this too, so it draws exactly what the app draws.</summary>
    internal static Shell CreateShell(IApplication app, Theme theme, IWingetClient client, WingmanSettings settings)
    {
        var shell = new Shell(app, theme, client, settings);
        shell.SetTabs(
        [
            new InstalledTab(shell, client),
            new DiscoverTab(shell, client),
            new UpdatesTab(shell, client),
            new PlaceholderTab(theme, "History", "History: coming in phase 2"),
            new PlaceholderTab(theme, "Settings", "Settings: coming in phase 2"),
        ]);
        LoadWingetVersion(shell, client);
        shell.ReloadPins();
        return shell;
    }

    private static string? DataDirectoryOverride()
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
