namespace Wingman.Core.Bundles;

/// <summary>
/// What <see cref="BundleImportPlanner"/> proposes to do with one bundle package on import.
/// </summary>
public enum ImportAction
{
    Install,
    Upgrade,
    Keep,
    Skip,
    Incompatible,
}
