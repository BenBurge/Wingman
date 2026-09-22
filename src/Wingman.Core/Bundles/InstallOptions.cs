namespace Wingman.Core.Bundles;

/// <summary>
/// Per-package install options, mirroring UniGetUI's <c>InstallOptions</c> schema verbatim (including
/// its property names and casing) so bundles stay interchangeable between Wingman and UniGetUI.
/// </summary>
public sealed class InstallOptions
{
    public bool SkipHashCheck { get; set; }

    public bool InteractiveInstallation { get; set; }

    public bool RunAsAdministrator { get; set; }

    public bool PreRelease { get; set; }

    public bool SkipMinorUpdates { get; set; }

    public int SkipMinorUpdatesLevel { get; set; }

    public bool RemoveDataOnUninstall { get; set; }

    public bool UninstallPreviousVersionsOnUpdate { get; set; }

    public bool AbortOnPreInstallFail { get; set; }

    public bool AbortOnPreUpdateFail { get; set; }

    public bool AbortOnPreUninstallFail { get; set; }

    public bool AutoUpdatePackage { get; set; }

    public string Architecture { get; set; } = "";

    public string InstallationScope { get; set; } = "";

    public string CustomInstallLocation { get; set; } = "";

    public string Version { get; set; } = "";

    public string PreInstallCommand { get; set; } = "";

    public string PostInstallCommand { get; set; } = "";

    public string PreUpdateCommand { get; set; } = "";

    public string PostUpdateCommand { get; set; } = "";

    public string PreUninstallCommand { get; set; } = "";

    public string PostUninstallCommand { get; set; } = "";

    public List<string> CustomParameters_Install { get; set; } = [];

    public List<string> CustomParameters_Update { get; set; } = [];

    public List<string> CustomParameters_Uninstall { get; set; } = [];

    public List<string> KillBeforeOperation { get; set; } = [];

    public bool OverridesNextLevelOpts { get; set; }

    public bool CustomInstallLocationIsExplicit { get; set; }

    /// <summary>
    /// True when every property is still at its default value, meaning nothing has actually been
    /// customized for this package.
    /// </summary>
    public bool IsDefault() => Equals(new InstallOptions());

    public override bool Equals(object? obj) =>
        obj is InstallOptions other
        && SkipHashCheck == other.SkipHashCheck
        && InteractiveInstallation == other.InteractiveInstallation
        && RunAsAdministrator == other.RunAsAdministrator
        && PreRelease == other.PreRelease
        && SkipMinorUpdates == other.SkipMinorUpdates
        && SkipMinorUpdatesLevel == other.SkipMinorUpdatesLevel
        && RemoveDataOnUninstall == other.RemoveDataOnUninstall
        && UninstallPreviousVersionsOnUpdate == other.UninstallPreviousVersionsOnUpdate
        && AbortOnPreInstallFail == other.AbortOnPreInstallFail
        && AbortOnPreUpdateFail == other.AbortOnPreUpdateFail
        && AbortOnPreUninstallFail == other.AbortOnPreUninstallFail
        && AutoUpdatePackage == other.AutoUpdatePackage
        && Architecture == other.Architecture
        && InstallationScope == other.InstallationScope
        && CustomInstallLocation == other.CustomInstallLocation
        && Version == other.Version
        && PreInstallCommand == other.PreInstallCommand
        && PostInstallCommand == other.PostInstallCommand
        && PreUpdateCommand == other.PreUpdateCommand
        && PostUpdateCommand == other.PostUpdateCommand
        && PreUninstallCommand == other.PreUninstallCommand
        && PostUninstallCommand == other.PostUninstallCommand
        && CustomParameters_Install.SequenceEqual(other.CustomParameters_Install)
        && CustomParameters_Update.SequenceEqual(other.CustomParameters_Update)
        && CustomParameters_Uninstall.SequenceEqual(other.CustomParameters_Uninstall)
        && KillBeforeOperation.SequenceEqual(other.KillBeforeOperation)
        && OverridesNextLevelOpts == other.OverridesNextLevelOpts
        && CustomInstallLocationIsExplicit == other.CustomInstallLocationIsExplicit;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SkipHashCheck);
        hash.Add(InteractiveInstallation);
        hash.Add(RunAsAdministrator);
        hash.Add(PreRelease);
        hash.Add(SkipMinorUpdates);
        hash.Add(SkipMinorUpdatesLevel);
        hash.Add(RemoveDataOnUninstall);
        hash.Add(UninstallPreviousVersionsOnUpdate);
        hash.Add(AbortOnPreInstallFail);
        hash.Add(AbortOnPreUpdateFail);
        hash.Add(AbortOnPreUninstallFail);
        hash.Add(AutoUpdatePackage);
        hash.Add(Architecture);
        hash.Add(InstallationScope);
        hash.Add(CustomInstallLocation);
        hash.Add(Version);
        hash.Add(OverridesNextLevelOpts);
        hash.Add(CustomInstallLocationIsExplicit);
        return hash.ToHashCode();
    }
}
