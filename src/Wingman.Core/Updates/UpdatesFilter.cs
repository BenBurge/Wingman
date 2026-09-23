using Wingman.Core.Models;
using Wingman.Core.Options;

namespace Wingman.Core.Updates;

/// <summary>The Updates tab's rows plus counts of what was left out and why.</summary>
public sealed record UpdatesView(IReadOnlyList<UpdateRow> Visible, int HeldCount, int ExcludedCount, int SkippedCount);

/// <summary>
/// Applies <see cref="UpdatePolicyResolver"/> to a full upgrade list for the Updates tab: excluded
/// and skip-this-version rows are dropped, held rows stay visible so the user can see what is stuck.
/// </summary>
public static class UpdatesFilter
{
    public static UpdatesView Apply(
        IReadOnlyList<PackageRow> upgradeRows, IReadOnlyList<Pin> pins, PackageOptionsStore options)
    {
        var visible = new List<UpdateRow>(upgradeRows.Count);
        var heldCount = 0;
        var excludedCount = 0;
        var skippedCount = 0;

        foreach (var row in upgradeRows)
        {
            var updatesOptions = options.GetUpdatesOptions(row.Id);
            var policy = UpdatePolicyResolver.Resolve(row, pins, updatesOptions);

            switch (policy)
            {
                case UpdatePolicyKind.Exclude:
                    excludedCount++;
                    continue;
                case UpdatePolicyKind.SkipVersion:
                    skippedCount++;
                    continue;
                case UpdatePolicyKind.Hold:
                    heldCount++;
                    break;
            }

            visible.Add(new UpdateRow(row, policy));
        }

        return new UpdatesView(visible, heldCount, excludedCount, skippedCount);
    }
}
