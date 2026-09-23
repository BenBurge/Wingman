using System.Runtime.Versioning;

namespace Wingman.Windows.Tray;

/// <summary>The <c>wingman tray</c> process: one notification-area icon per user session.</summary>
[SupportedOSPlatform("windows")]
public static class TrayApp
{
    private const string MutexName = @"Local\Wingman.Tray";

    /// <summary>
    /// Shows the tray icon and pumps messages until Quit, <c>WM_CLOSE</c>, or Ctrl+C. Returns 0
    /// at once when another tray process already runs in this session.
    /// </summary>
    /// <param name="exePath">The <c>wingman</c> executable that click and menu actions launch.</param>
    /// <param name="dataDirectory">The folder holding <c>state.json</c> and <c>settings.json</c>.</param>
    public static int Run(string exePath, string dataDirectory)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            TrayWindow.Log("another tray process is running; exiting");
            return 0;
        }

        try
        {
            using var window = new TrayWindow(exePath, dataDirectory);
            return window.RunMessageLoop();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
