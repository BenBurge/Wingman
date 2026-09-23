using Wingman.Core.Settings;

namespace Wingman.Core.Operations;

/// <summary>
/// How a <see cref="BatchRunner"/> run reacts to a failed operation and whether it elevates.
/// </summary>
/// <param name="ContinueOnFailure">Keeps running the remaining operations after one fails; when
/// false, the rest are reported as <see cref="OperationCanceled"/> and not run.</param>
/// <param name="ElevationMode">Which operations run through the one elevated helper opened for
/// the whole batch; the rest run in-process.</param>
/// <param name="ProcessIsElevated">This process already runs as administrator, so every operation
/// runs in-process and the helper is never started.</param>
public sealed record BatchOptions(
    bool ContinueOnFailure = true,
    ElevationMode ElevationMode = ElevationMode.Auto,
    bool ProcessIsElevated = false);
