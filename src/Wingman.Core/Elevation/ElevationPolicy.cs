using Wingman.Core.Operations;
using Wingman.Core.Settings;

namespace Wingman.Core.Elevation;

/// <summary>
/// Decides which of a batch's operations run through the elevated helper, and so whether the
/// batch needs the helper at all.
/// </summary>
public static class ElevationPolicy
{
    /// <summary>
    /// True when <paramref name="plan"/> runs through the elevated helper: always when the user
    /// forced it, even under <see cref="ElevationMode.Never"/>, and otherwise as
    /// <see cref="ElevationHeuristic.Resolve"/> decides from the plan alone. An already elevated
    /// process runs everything in-process, since the helper would add nothing.
    /// </summary>
    public static bool UsesHelper(OperationPlan plan, ElevationMode mode, bool processIsElevated)
    {
        if (processIsElevated)
        {
            return false;
        }

        return plan.ForceElevation || ElevationHeuristic.Resolve(plan, details: null, mode, processIsElevated);
    }

    /// <summary>True when at least one queued operation runs through the elevated helper.</summary>
    public static bool NeedsHelper(OperationQueue queue, ElevationMode mode, bool processIsElevated) =>
        NeedsHelper(queue.Items, mode, processIsElevated);

    /// <inheritdoc cref="NeedsHelper(OperationQueue, ElevationMode, bool)"/>
    public static bool NeedsHelper(IReadOnlyList<QueuedOperation> operations, ElevationMode mode, bool processIsElevated) =>
        operations.Any(operation => UsesHelper(operation.Plan, mode, processIsElevated));
}
