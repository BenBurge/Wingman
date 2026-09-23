using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

/// <summary>
/// Stands in for <see cref="ProcessRunner"/>: records every call and answers with scripted output
/// and a scripted exit code.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public string StandardOutput { get; init; } = "";

    public IReadOnlyList<string> StreamedLines { get; init; } = [];

    public int ExitCode { get; init; }

    public List<(string FileName, string[] Arguments)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls.Add((fileName, [.. arguments]));
        return Task.FromResult(new ProcessResult(ExitCode, StandardOutput, ""));
    }

    public Task<int> RunStreamingAsync(
        string fileName, IReadOnlyList<string> arguments, IProgress<string> output, CancellationToken ct)
    {
        Calls.Add((fileName, [.. arguments]));
        foreach (var line in StreamedLines)
        {
            output.Report(line);
        }

        return Task.FromResult(ExitCode);
    }
}
