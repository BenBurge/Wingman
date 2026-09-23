namespace Wingman.Core.Winget;

/// <summary>
/// The result of running an external process to completion.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs an external process and captures its output. Exists so <see cref="WingetCliClient"/> can be
/// tested without invoking <c>winget.exe</c>, which is not available on this codebase's development
/// machines.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process to completion and returns everything it wrote.
    /// </summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);

    /// <summary>
    /// Runs the process to completion, reporting each stdout and stderr line to
    /// <paramref name="output"/> as it arrives, and returns the exit code. Lines from the two
    /// streams may interleave. Canceling <paramref name="ct"/> kills the process and its children
    /// and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<int> RunStreamingAsync(string fileName, IReadOnlyList<string> arguments, IProgress<string> output, CancellationToken ct);
}
