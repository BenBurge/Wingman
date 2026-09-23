using Wingman.Core.Models;

namespace Wingman.Core.Operations;

/// <summary>
/// One event reported by <see cref="BatchRunner.RunAsync"/>. <c>Index</c> is the operation's
/// position in the list passed to the run.
/// </summary>
public abstract record BatchProgress;

public sealed record BatchStarted(string BatchId, int Total) : BatchProgress;

public sealed record OperationStarted(int Index, QueuedOperation Operation) : BatchProgress;

/// <summary>A line printed by the operation's pre-command, winget, or post-command.</summary>
public sealed record OperationLine(int Index, string Text) : BatchProgress;

/// <param name="Skipped">The pre-command failed with abort-on-failure set, so winget never ran.</param>
public sealed record OperationFinished(int Index, QueuedOperation Operation, OperationResult Result, bool Skipped)
    : BatchProgress;

/// <summary>
/// An operation that was never started, because the batch was canceled, an earlier operation
/// failed with <see cref="BatchOptions.ContinueOnFailure"/> off, or it needed the elevated helper
/// and the helper could not be started; in the last case an <see cref="OperationLine"/> with the
/// reason comes first.
/// </summary>
/// <param name="ElevationReason">The line reported first when the elevated helper could not be
/// had, such as <c>Canceled by UAC</c>; null for the other reasons.</param>
public sealed record OperationCanceled(int Index, QueuedOperation Operation, string? ElevationReason = null) : BatchProgress;

/// <summary>
/// A step in the elevated helper's life for this batch: <c>requesting</c>, <c>connected</c>,
/// <c>declined</c>, <c>timed out after &lt;n&gt; s</c>, <c>canceled</c> (the batch was canceled
/// while waiting for the prompt), <c>failed: &lt;reason&gt;</c>, or <c>closed</c>. A batch that
/// never starts the helper reports one of <c>not needed</c>, <c>off</c>
/// (<see cref="Settings.ElevationMode.Never"/> kept an operation that needed it in-process), or
/// <c>running as administrator</c> (this process is already elevated), and nothing when the helper
/// was needed but no factory can start it.
/// </summary>
public sealed record ElevationState(string State) : BatchProgress;

/// <summary>
/// Reported about once a second between <c>requesting</c> and the elevated helper's next state,
/// with how long the batch has waited for it, so the screen can count the wait for the prompt.
/// </summary>
public sealed record ElevationWaiting(TimeSpan Elapsed) : BatchProgress;

public sealed record BatchFinished(BatchSummary Summary) : BatchProgress;
