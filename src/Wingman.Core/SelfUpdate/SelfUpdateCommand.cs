using Wingman.Core.Setup;

namespace Wingman.Core.SelfUpdate;

/// <summary>
/// The command that upgrades Wingman itself. <see cref="ISelfUpdateStarter"/> runs it detached
/// in a hidden <c>cmd.exe</c> so the upgrade can replace the running executable after this
/// process exits; nothing here executes anything.
/// </summary>
public static class SelfUpdateCommand
{
    // winget is an app execution alias under %LOCALAPPDATA%\Microsoft\WindowsApps, not a System32
    // program, so it is the one tool started by name.
    private const string WingetUpgradeCommand =
        $"winget upgrade --id {SelfUpdateChecker.PackageId} --exact --source winget " +
        "--accept-source-agreements --accept-package-agreements --disable-interactivity";

    /// <summary>
    /// The <c>cmd.exe</c> argument string that waits two seconds, runs
    /// <see cref="WingetUpgradeCommand"/>, and, when <paramref name="restartTray"/>, starts the tray
    /// again once winget is done. It is one string for <c>ProcessStartInfo.Arguments</c> rather than
    /// an argv, because <c>ArgumentList</c> would escape the inner quotes with backslashes, which
    /// <c>cmd.exe</c> does not understand; <c>/s</c> makes cmd strip only the outer pair.
    /// </summary>
    /// <param name="systemDirectory">The System32 folder <c>timeout.exe</c> and <c>conhost.exe</c> are started from.</param>
    /// <param name="exePath">The Wingman executable the restarted tray runs.</param>
    /// <param name="restartTray">The tray was running and was closed so winget could replace the executable.</param>
    public static string DetachedUpgradeArguments(string systemDirectory, string exePath, bool restartTray)
    {
        // The delay gives this process time to exit and release its executable before winget
        // tries to replace it; cmd.exe carries the steps because the running app cannot wait on
        // its own exit.
        var timeout = SystemTool.PathIn(systemDirectory, "timeout.exe");
        var command = $"\"{timeout}\" /t 2 /nobreak >nul & {WingetUpgradeCommand}";

        if (restartTray)
        {
            // `start` detaches the tray from this cmd.exe, so cmd exits once it has launched the
            // tray instead of staying alive (and visible in Task Manager) for as long as the tray
            // runs. The empty "" is the window title `start` requires when its first argument,
            // the program to run, is itself quoted.
            var conhost = SystemTool.PathIn(systemDirectory, "conhost.exe");
            command += $" & start \"\" \"{conhost}\" --headless \"{exePath}\" tray";
        }

        return $"/d /s /c \"{command}\"";
    }

    /// <summary>The command to show on the status line, without the detachment plumbing.</summary>
    public static string Describe() => WingetUpgradeCommand;
}
