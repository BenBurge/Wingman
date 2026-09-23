using Wingman.Core.Operations;

namespace Wingman.Core.Elevation;

/// <summary>
/// Decides whether a batch needs the elevated helper.
/// </summary>
public static class ElevationPolicy
{
    /// <summary>
    /// True when the user allows automatic elevation and at least one queued operation needs it.
    /// </summary>
    public static bool NeedsHelper(OperationQueue queue, bool autoElevate) =>
        autoElevate && queue.ElevatedCount > 0;
}
