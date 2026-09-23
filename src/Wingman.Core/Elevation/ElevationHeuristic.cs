using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Settings;

namespace Wingman.Core.Elevation;

/// <summary>
/// Guesses from a package's installer whether winget will need administrator rights, so an
/// operation that would otherwise elevate the installer mid-run goes through the elevated helper.
/// </summary>
/// <remarks>
/// When winget elevates an installer itself, a UAC broker such as Admin By Request can intercept
/// the prompt: winget then fails with <c>0x8007029C</c> and the installer runs detached after the
/// approval. Running the whole operation elevated avoids that hand-off.
/// </remarks>
public static class ElevationHeuristic
{
    private const string ScopeField = "Installer.Scope";

    private static readonly HashSet<string> MachineWideTypes =
        new(["msi", "wix", "burn", "exe", "inno", "nullsoft"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UnelevatedTypes =
        new(["msix", "appx", "portable", "zip"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True for installer types that usually install machine-wide, unless the manifest scopes the
    /// installer to the user; false for types that never elevate; null when the type is missing
    /// or unrecognized.
    /// </summary>
    public static bool? NeedsElevation(PackageDetails details)
    {
        var installerType = details.InstallerType.Trim();
        if (UnelevatedTypes.Contains(installerType))
        {
            return false;
        }

        if (!MachineWideTypes.Contains(installerType))
        {
            return null;
        }

        var isUserScoped = details.AdditionalFields.TryGetValue(ScopeField, out var scope)
            && string.Equals(scope.Trim(), "user", StringComparison.OrdinalIgnoreCase);
        return !isUserScoped;
    }

    /// <summary>
    /// Whether <paramref name="plan"/> should run through the elevated helper under
    /// <paramref name="mode"/>. An already elevated process never needs the helper.
    /// </summary>
    /// <param name="details">The package's <c>winget show</c> details, or null when they were not
    /// fetched; only <see cref="ElevationMode.Auto"/> reads them.</param>
    public static bool Resolve(OperationPlan plan, PackageDetails? details, ElevationMode mode, bool processIsElevated)
    {
        if (processIsElevated)
        {
            return false;
        }

        switch (mode)
        {
            case ElevationMode.Never:
                return false;
            case ElevationMode.Always:
                return true;
            default:
                var installerNeedsElevation = details is not null && NeedsElevation(details) == true;
                return plan.RequiresElevation || installerNeedsElevation;
        }
    }
}
