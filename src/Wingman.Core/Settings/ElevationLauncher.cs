using System.Text.Json.Serialization;

namespace Wingman.Core.Settings;

/// <summary>
/// Which program the elevation prompt is raised for, when Wingman starts its elevated helper or
/// restarts itself as administrator.
/// </summary>
public enum ElevationLauncher
{
    /// <summary>Runs <c>wingman.exe</c> itself with the <c>runas</c> verb.</summary>
    [JsonStringEnumMemberName("direct")]
    Direct,

    /// <summary>
    /// Runs Windows PowerShell 5.1 with the <c>runas</c> verb and has it start <c>wingman.exe</c>,
    /// for environments whose admin-approval tool, such as Admin By Request, whitelists PowerShell
    /// but not Wingman.
    /// </summary>
    [JsonStringEnumMemberName("powerShell")]
    PowerShell,
}
