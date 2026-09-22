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
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}
