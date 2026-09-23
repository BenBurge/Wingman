using Terminal.Gui.App;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui.Tabs;

namespace Wingman.Tui;

/// <summary>Entry point for the terminal UI.</summary>
public static class WingmanApp
{
    public static void Run(IWingetClient client)
    {
        var theme = Theme.ByName(SettingsStore.CreateDefault().Load().Theme);

        using var app = Application.Create();
        app.Init();

        var shell = CreateShell(app, theme, client);
        app.Run(shell.Window);
        shell.Window.Dispose();
    }

    /// <summary>Builds the window and every tab. <c>tools/TuiHarness</c> calls this too, so it draws exactly what the app draws.</summary>
    internal static Shell CreateShell(IApplication app, Theme theme, IWingetClient client)
    {
        var shell = new Shell(app, theme);
        shell.SetTabs(
        [
            new InstalledTab(shell, client),
            new DiscoverTab(shell, client),
            new UpdatesTab(shell, client),
            new PlaceholderTab(theme, "History", "History: coming in phase 2"),
            new PlaceholderTab(theme, "Settings", "Settings: coming in phase 2"),
        ]);
        LoadWingetVersion(shell, client);
        return shell;
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
