namespace Wingman.Core.Operations;

/// <summary>
/// How a <see cref="BatchRunner"/> run reacts to a failed operation.
/// </summary>
/// <param name="ContinueOnFailure">Keeps running the remaining operations after one fails; when
/// false, the rest are reported as <see cref="OperationCanceled"/> and not run.</param>
public sealed record BatchOptions(bool ContinueOnFailure = true);
