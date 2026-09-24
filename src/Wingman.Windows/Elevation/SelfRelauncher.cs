using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Wingman.Core.Elevation;
using Wingman.Core.Settings;

namespace Wingman.Windows.Elevation;

/// <summary>Starts this executable again through a UAC prompt, for the TUI's Restart as administrator.</summary>
[SupportedOSPlatform("windows")]
public static class SelfRelauncher
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Starts <see cref="Environment.ProcessPath"/> elevated with <paramref name="args"/>, directly
    /// or in a visible PowerShell window as <paramref name="launcher"/> says; true once it has
    /// started, so the caller can quit, and false when the user declined the prompt.
    /// </summary>
    /// <exception cref="Win32Exception">The process could not be started for another reason.</exception>
    public static bool TryRestartAsAdministrator(ElevationLauncher launcher, IReadOnlyList<string> args)
    {
        var (fileName, arguments) = ElevationLaunch.Build(
            launcher, Environment.SystemDirectory, Environment.ProcessPath!, args, hidden: false);
        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal,
        };

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
