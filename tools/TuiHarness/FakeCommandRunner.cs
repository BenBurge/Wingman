using Wingman.Core.Winget;

namespace TuiHarness;

/// <summary>
/// Stands in for the process runner that runs packages' pre- and post-commands, so a harness run
/// never starts a shell: every command prints one line and succeeds.
/// </summary>
internal sealed class FakeCommandRunner : IProcessRunner
{
    public const string OutputLine = "(harness: command not run)";

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct) =>
        Task.FromResult(new ProcessResult(0, "", ""));

    public Task<int> RunStreamingAsync(
        string fileName, IReadOnlyList<string> arguments, IProgress<string> output, CancellationToken ct)
    {
        output.Report(OutputLine);
        return Task.FromResult(0);
    }
}
