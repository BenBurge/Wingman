using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wingman.Windows;

/// <summary>
/// Whether this process has a console window to draw the TUI in, and a way to open a new one when
/// it does not, as when a toast or the tray started Wingman without a console.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TerminalWindow
{
    public static bool HasConsoleWindow() => GetConsoleWindow() != 0;

    /// <summary>
    /// Starts <paramref name="argv"/> through the shell so a console program gets a window of its
    /// own; false when it could not be started.
    /// </summary>
    public static bool TryStart(string[] argv)
    {
        var startInfo = new ProcessStartInfo(argv[0]) { UseShellExecute = true };
        foreach (var argument in argv[1..])
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetConsoleWindow();
}
