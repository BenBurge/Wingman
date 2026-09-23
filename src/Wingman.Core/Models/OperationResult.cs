namespace Wingman.Core.Models;

/// <summary>
/// The outcome of a winget operation that changes the machine.
/// </summary>
/// <param name="Succeeded">True exactly when <paramref name="ExitCode"/> is 0.</param>
/// <param name="Log">Every stdout and stderr line winget printed, in the order it arrived.</param>
public sealed record OperationResult(int ExitCode, bool Succeeded, TimeSpan Duration, IReadOnlyList<string> Log);
