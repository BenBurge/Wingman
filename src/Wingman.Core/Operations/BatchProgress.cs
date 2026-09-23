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
/// An operation that was never started, because the batch was canceled or an earlier operation
/// failed with <see cref="BatchOptions.ContinueOnFailure"/> off.
/// </summary>
public sealed record OperationCanceled(int Index, QueuedOperation Operation) : BatchProgress;

public sealed record BatchFinished(BatchSummary Summary) : BatchProgress;
