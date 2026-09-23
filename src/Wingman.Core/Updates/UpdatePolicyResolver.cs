using Wingman.Core.Bundles;
using Wingman.Core.Models;

namespace Wingman.Core.Updates;

/// <summary>
/// Decides how Wingman treats one package's available update, combining winget pins with the
/// per-package <see cref="UpdatesOptions"/> Wingman stores itself.
/// </summary>
public static class UpdatePolicyResolver
{
    public static UpdatePolicyKind Resolve(PackageRow row, IReadOnlyList<Pin> pins, UpdatesOptions options)
    {
        // Only a blocking pin counts as a hold: winget still upgrades a gating pin within its
        // range, and a plain pin only excludes the package from `upgrade --all`, so treating
        // either as a hold would report packages as stuck that winget would happily move.
        var hasBlockingPin = pins.Any(pin =>
            string.Equals(pin.Id, row.Id, StringComparison.OrdinalIgnoreCase) && pin.PinType == PinType.Blocking);
        if (hasBlockingPin)
        {
            return UpdatePolicyKind.Hold;
        }

        if (options.UpdatesIgnored)
        {
            return UpdatePolicyKind.Exclude;
        }

        if (options.IgnoredVersion.Length > 0
            && string.Equals(options.IgnoredVersion, row.AvailableVersion, StringComparison.OrdinalIgnoreCase))
        {
            return UpdatePolicyKind.SkipVersion;
        }

        return UpdatePolicyKind.Update;
    }
}
