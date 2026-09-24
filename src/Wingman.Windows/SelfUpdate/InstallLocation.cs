using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Wingman.Windows.SelfUpdate;

/// <summary>Reads where the Wingman installer put its files, from its uninstall entry.</summary>
[SupportedOSPlatform("windows")]
public static class InstallLocation
{
    // Inno Setup names the entry after installer/wingman.iss's AppId plus "_is1"; the two must match.
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{FC7D592F-76BD-4409-9F32-1C9EAF29A091}_is1";

    /// <summary>The entry's <c>InstallLocation</c>, or null when Wingman was never installed by the installer.</summary>
    public static string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
