namespace Wingman.Core.Operations;

/// <summary>
/// The outcome of one <see cref="BatchRunner"/> run.
/// </summary>
/// <param name="Failed">Operations that ran and did not succeed, including those skipped because
/// their pre-command failed.</param>
/// <param name="Canceled">Operations that never ran, because of a cancel, an earlier failure
/// with <see cref="BatchOptions.ContinueOnFailure"/> off, or an elevated helper that could not be
/// started.</param>
public sealed record BatchSummary(
    string BatchId,
    int Total,
    int Succeeded,
    int Failed,
    int Canceled,
    TimeSpan Duration);
