using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Wingman.Windows.Elevation;

/// <summary>Starts this executable again through a UAC prompt, for the TUI's Restart as administrator.</summary>
[SupportedOSPlatform("windows")]
public static class SelfRelauncher
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Starts <see cref="Environment.ProcessPath"/> elevated with <paramref name="args"/>; true once
    /// it has started, so the caller can quit, and false when the user declined the prompt.
    /// </summary>
    /// <exception cref="Win32Exception">The process could not be started for another reason.</exception>
    public static bool TryRestartAsAdministrator(IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }
}
