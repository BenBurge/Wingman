namespace Wingman.Core.Operations;

/// <summary>
/// How a <see cref="BatchRunner"/> run reacts to a failed operation and whether it elevates.
/// </summary>
/// <param name="ContinueOnFailure">Keeps running the remaining operations after one fails; when
/// false, the rest are reported as <see cref="OperationCanceled"/> and not run.</param>
/// <param name="AutoElevate">Runs the operations that need elevation through one elevated helper
/// for the whole batch; when false, they run in-process and winget prompts per installer.</param>
public sealed record BatchOptions(bool ContinueOnFailure = true, bool AutoElevate = true);
