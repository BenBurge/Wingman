using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Wingman.Core.Settings;

namespace Wingman.Windows;

/// <summary>Reads the "app mode" choice from Windows' personalization settings.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsThemeDetector : IThemeDetector
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // A DWORD, 1 when apps use the light theme and 0 when they use the dark one; older Windows versions lack it.
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    public bool? IsLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightTheme) is int value ? value != 0 : null;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
