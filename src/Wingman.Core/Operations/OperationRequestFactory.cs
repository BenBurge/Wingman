using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Settings;

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
