using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Core.Operations;

/// <summary>
/// Turns a package's <see cref="InstallOptions"/> and the user's <see cref="WingmanSettings"/> into
/// the <see cref="OperationRequest"/> and elevation/pre-post-command metadata for one winget run.
/// </summary>
public static class OperationRequestFactory
{
    public static OperationPlan Create(OperationKind kind, PackageRow row, WingmanSettings settings, InstallOptions options)
    {
        var scope = EffectiveScope(options, settings);
        var isUninstall = kind == OperationKind.Uninstall;

        var request = new OperationRequest(
            Id: row.Id,
            Version: kind == OperationKind.Install && !string.IsNullOrEmpty(options.Version) ? options.Version : null,
            Scope: scope,
            Architecture: !isUninstall && !string.IsNullOrEmpty(options.Architecture) ? options.Architecture : null,
            Interactive: options.InteractiveInstallation,
            SkipHashCheck: !isUninstall && options.SkipHashCheck,
            Force: false,
            CustomArguments: EffectiveCustomArguments(kind, options));

        var requiresElevation = options.RunAsAdministrator || string.Equals(scope, "machine", StringComparison.OrdinalIgnoreCase);

        var (preCommand, postCommand, abortOnPreFail) = kind switch
        {
            OperationKind.Install => (options.PreInstallCommand, options.PostInstallCommand, options.AbortOnPreInstallFail),
            OperationKind.Upgrade => (options.PreUpdateCommand, options.PostUpdateCommand, options.AbortOnPreUpdateFail),
            OperationKind.Uninstall => (options.PreUninstallCommand, options.PostUninstallCommand, options.AbortOnPreUninstallFail),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return new OperationPlan(kind, request, requiresElevation, preCommand, postCommand, abortOnPreFail);
    }

    /// <summary>
    /// The plan that takes <paramref name="row"/>'s package to <paramref name="version"/>, picked
    /// from the version picker: an upgrade pinned to it when it is newer than the installed version,
    /// and an install pinned to it when the package is not installed. winget's <c>upgrade</c>
    /// refuses an older version, so going back is an install with <c>--force</c>, labeled
    /// <see cref="OperationPlan.DowngradeLabel"/>.
    /// </summary>
    /// <param name="row">The installed row when <paramref name="isInstalled"/>, so its
    /// <see cref="PackageRow.Version"/> is the installed version; any row for the package otherwise.</param>
    /// <returns>Null when <paramref name="version"/> is the installed version, which leaves nothing to run.</returns>
    public static OperationPlan? CreateForVersion(
        PackageRow row, string version, WingmanSettings settings, InstallOptions options, bool isInstalled)
    {
        if (!isInstalled)
        {
            return WithVersion(Create(OperationKind.Install, row, settings, options), version);
        }

        var comparison = VersionComparer.Instance.Compare(version, row.Version);
        if (comparison == 0)
        {
            return null;
        }

        if (comparison > 0)
        {
            return WithVersion(Create(OperationKind.Upgrade, row, settings, options), version);
        }

        var install = Create(OperationKind.Install, row, settings, options);
        var request = install.Request with { Version = version, Force = true };
        return install with { Request = request, Label = OperationPlan.DowngradeLabel };
    }

    private static OperationPlan WithVersion(OperationPlan plan, string version) =>
        plan with { Request = plan.Request with { Version = version } };

    private static string? EffectiveScope(InstallOptions options, WingmanSettings settings)
    {
        if (!string.IsNullOrEmpty(options.InstallationScope))
        {
            return options.InstallationScope;
        }

        return string.IsNullOrEmpty(settings.DefaultScope) ? null : settings.DefaultScope;
    }

    private static IReadOnlyList<string>? EffectiveCustomArguments(OperationKind kind, InstallOptions options)
    {
        var customArguments = kind switch
        {
            OperationKind.Install => options.CustomParameters_Install,
            OperationKind.Upgrade => options.CustomParameters_Update,
            OperationKind.Uninstall => options.CustomParameters_Uninstall,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return customArguments.Count == 0 ? null : customArguments;
    }
}
