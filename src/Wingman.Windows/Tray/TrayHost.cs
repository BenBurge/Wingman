namespace Wingman.Windows.Tray;

/// <summary>The tray entry point for callers on any OS, without a platform guard of their own.</summary>
public static class TrayHost
{
    /// <summary>
    /// <see cref="TrayApp.Run(string, string)"/> on Windows, returning its exit code; null elsewhere,
    /// where there is no notification area.
    /// </summary>
    public static int? Run(string exePath, string dataDirectory) =>
        OperatingSystem.IsWindows() ? TrayApp.Run(exePath, dataDirectory) : null;
}
