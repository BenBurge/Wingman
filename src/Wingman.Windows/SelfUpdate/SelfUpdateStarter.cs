using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Wingman.Core.SelfUpdate;
using static Wingman.Windows.Tray.NativeMethods;

namespace Wingman.Windows.SelfUpdate;

/// <summary>
/// Starts the self-upgrade in a hidden <c>cmd.exe</c> that keeps running after Wingman exits,
/// because winget cannot replace the executable while this process holds it open. A running tray
/// holds it open too, so the tray is closed first and started again after winget finishes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SelfUpdateStarter : ISelfUpdateStarter
{
    // The class TrayWindow registers for its message-only window.
    private const string TrayWindowClassName = "Wingman.Tray";

    private static readonly TimeSpan TrayExitTimeout = TimeSpan.FromSeconds(3);

    public void StartDetachedUpgrade()
    {
        var systemDirectory = Environment.SystemDirectory;
        var exePath = Environment.ProcessPath ?? "wingman.exe";
        var trayWasRunning = CloseTray();

        var arguments = SelfUpdateCommand.DetachedUpgradeArguments(systemDirectory, exePath, trayWasRunning);
        var startInfo = new ProcessStartInfo(Path.Combine(systemDirectory, "cmd.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>
    /// Asks the tray to close and waits a short while for its process to exit. Returns true when a
    /// tray window was found, so the upgrade restarts it even if it was slow to exit.
    /// </summary>
    private static bool CloseTray()
    {
        var hwnd = FindWindowExW(HWND_MESSAGE, 0, TrayWindowClassName, null);
        if (hwnd == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        PostMessageW(hwnd, WM_CLOSE, 0, 0);
        WaitForExit(processId);
        return true;
    }

    private static void WaitForExit(uint processId)
    {
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var tray = Process.GetProcessById((int)processId);
            tray.WaitForExit(TrayExitTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The tray already exited, or its process cannot be opened; winget reports a locked
            // executable itself if it is still running.
        }
    }
}
