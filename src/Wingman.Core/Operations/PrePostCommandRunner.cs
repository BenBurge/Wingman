using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Operations;

/// <summary>
/// Runs an <see cref="OperationPlan"/>'s pre- and post-commands through an <see cref="IProcessRunner"/>,
/// streaming their output the same way a winget operation does.
/// </summary>
public sealed class PrePostCommandRunner
{
    private readonly IProcessRunner _processRunner;

    public PrePostCommandRunner(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <summary>
    /// Runs <see cref="OperationPlan.PreCommand"/>, or does nothing and returns <c>null</c> when it is
    /// empty. A non-zero exit is only ever reported here; <see cref="ShouldSkipOperation"/> is what
    /// decides whether the caller skips the operation.
    /// </summary>
    public Task<int?> RunPreAsync(OperationPlan plan, IProgress<string> output, CancellationToken ct) =>
        RunAsync(plan.PreCommand, plan, output, isPost: false, ct);

    /// <summary>
    /// Runs <see cref="OperationPlan.PostCommand"/>, or does nothing and returns <c>null</c> when it is
    /// empty. A non-zero exit is reported but never changes the operation's result.
    /// </summary>
    public Task<int?> RunPostAsync(OperationPlan plan, IProgress<string> output, CancellationToken ct) =>
        RunAsync(plan.PostCommand, plan, output, isPost: true, ct);

    public static bool ShouldSkipOperation(OperationPlan plan, int? preExitCode) =>
        plan.AbortOnPreFail && preExitCode is > 0 or < 0;

    public static OperationResult SkippedResult(int preExitCode, IReadOnlyList<string> log) =>
        new(preExitCode, false, TimeSpan.Zero, log);

    internal static (string FileName, string[] Arguments) ShellFor(string command) =>
        ShellFor(command, OperatingSystem.IsWindows());

    /// <summary>
    /// Takes <paramref name="isWindows"/> as a parameter, rather than reading it internally, so tests
    /// can cover both shells regardless of the host OS they run on.
    /// </summary>
    internal static (string FileName, string[] Arguments) ShellFor(string command, bool isWindows) =>
        isWindows
            ? ("cmd.exe", ["/d", "/c", command])
            : ("/bin/sh", ["-c", command]);

    private async Task<int?> RunAsync(
        string command, OperationPlan plan, IProgress<string> output, bool isPost, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        output.Report($"$ {command}");
        var (fileName, arguments) = ShellFor(command);
        var exitCode = await _processRunner.RunStreamingAsync(fileName, arguments, output, ct);

        if (exitCode != 0)
        {
            output.Report(FailureMessage(plan, exitCode, isPost));
        }

        return exitCode;
    }

    private static string FailureMessage(OperationPlan plan, int exitCode, bool isPost)
    {
        var verb = Verb(plan.Kind);
        if (isPost)
        {
            return $"Post-{verb} command failed with exit code {exitCode}.";
        }

        return plan.AbortOnPreFail
            ? $"Pre-{verb} command failed with exit code {exitCode}; skipped."
            : $"Pre-{verb} command failed with exit code {exitCode}; continuing.";
    }

    private static string Verb(OperationKind kind) => kind switch
    {
        OperationKind.Install => "install",
        OperationKind.Upgrade => "update",
        OperationKind.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
