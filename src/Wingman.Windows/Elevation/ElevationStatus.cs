using System.Runtime.Versioning;
using System.Security.Principal;

namespace Wingman.Windows.Elevation;

/// <summary>Whether this process already runs with administrator rights.</summary>
[SupportedOSPlatform("windows")]
public static class ElevationStatus
{
    /// <summary>
    /// True when the process token is in the Administrators group, which under UAC holds only for
    /// an elevated process; a filtered token of an administrator account reports false.
    /// </summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
