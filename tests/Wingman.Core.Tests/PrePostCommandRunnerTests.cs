using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Tests;

public class PrePostCommandRunnerTests
{
    private static OperationPlan Plan(
        OperationKind kind = OperationKind.Install,
        string preCommand = "",
        string postCommand = "",
        bool abortOnPreFail = false) =>
        new(
            Kind: kind,
            Request: new OperationRequest("Git.Git"),
            RequiresElevation: false,
            PreCommand: preCommand,
            PostCommand: postCommand,
            AbortOnPreFail: abortOnPreFail);

    [Fact]
    public void ShellFor_Windows_UsesCmd()
    {
        var (fileName, arguments) = PrePostCommandRunner.ShellFor("echo hi", isWindows: true);

        Assert.Equal("cmd.exe", fileName);
        Assert.Equal(["/d", "/c", "echo hi"], arguments);
    }

    [Fact]
    public void ShellFor_NonWindows_UsesSh()
    {
        var (fileName, arguments) = PrePostCommandRunner.ShellFor("echo hi", isWindows: false);

        Assert.Equal("/bin/sh", fileName);
        Assert.Equal(["-c", "echo hi"], arguments);
    }

    [Fact]
    public async Task RunPreAsync_RunsCommandThroughExpectedShell()
    {
        var runner = new FakeProcessRunner { StreamedLines = ["pre output"] };
        var commandRunner = new PrePostCommandRunner(runner);
        var plan = Plan(preCommand: "echo hi");

        var exitCode = await commandRunner.RunPreAsync(plan, new RecordingProgress(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        var (fileName, arguments) = Assert.Single(runner.Calls);
        var (expectedFileName, expectedArguments) = PrePostCommandRunner.ShellFor("echo hi");
        Assert.Equal(expectedFileName, fileName);
        Assert.Equal(expectedArguments, arguments);
    }

    [Fact]
    public async Task RunPreAsync_ReportsDollarLineBeforeForwardingOutput()
    {
        var runner = new FakeProcessRunner { StreamedLines = ["line one", "line two"] };
        var commandRunner = new PrePostCommandRunner(runner);
        var progress = new RecordingProgress();
        var plan = Plan(preCommand: "echo hi");

        await commandRunner.RunPreAsync(plan, progress, CancellationToken.None);

        Assert.Equal(["$ echo hi", "line one", "line two"], progress.Lines);
    }

    [Fact]
    public async Task RunPreAsync_EmptyCommand_ReturnsNullAndRunsNothing()
    {
        var runner = new FakeProcessRunner();
        var commandRunner = new PrePostCommandRunner(runner);
        var progress = new RecordingProgress();
        var plan = Plan(preCommand: "");

        var exitCode = await commandRunner.RunPreAsync(plan, progress, CancellationToken.None);

        Assert.Null(exitCode);
        Assert.Empty(runner.Calls);
        Assert.Empty(progress.Lines);
    }

    [Fact]
    public async Task RunPreAsync_NonZeroWithAbort_ReportsSkippedAndMarksSkip()
    {
        var runner = new FakeProcessRunner { ExitCode = 5 };
        var commandRunner = new PrePostCommandRunner(runner);
        var progress = new RecordingProgress();
        var plan = Plan(kind: OperationKind.Upgrade, preCommand: "echo hi", abortOnPreFail: true);

        var exitCode = await commandRunner.RunPreAsync(plan, progress, CancellationToken.None);

        Assert.Equal(5, exitCode);
        Assert.Contains("Pre-update command failed with exit code 5; skipped.", progress.Lines);
        Assert.True(PrePostCommandRunner.ShouldSkipOperation(plan, exitCode));
    }

    [Fact]
    public async Task RunPreAsync_NonZeroWithoutAbort_ReportsContinuingAndDoesNotSkip()
    {
        var runner = new FakeProcessRunner { ExitCode = 5 };
        var commandRunner = new PrePostCommandRunner(runner);
        var progress = new RecordingProgress();
        var plan = Plan(kind: OperationKind.Uninstall, preCommand: "echo hi", abortOnPreFail: false);

        var exitCode = await commandRunner.RunPreAsync(plan, progress, CancellationToken.None);

        Assert.Equal(5, exitCode);
        Assert.Contains("Pre-uninstall command failed with exit code 5; continuing.", progress.Lines);
        Assert.False(PrePostCommandRunner.ShouldSkipOperation(plan, exitCode));
    }

    [Fact]
    public void ShouldSkipOperation_ReturnsFalseWhenPreCommandDidNotRun()
    {
        var plan = Plan(abortOnPreFail: true);

        Assert.False(PrePostCommandRunner.ShouldSkipOperation(plan, preExitCode: null));
    }

    [Fact]
    public async Task RunPostAsync_EmptyCommand_ReturnsNullAndRunsNothing()
    {
        var runner = new FakeProcessRunner();
        var commandRunner = new PrePostCommandRunner(runner);
        var plan = Plan(postCommand: "");

        var exitCode = await commandRunner.RunPostAsync(plan, new RecordingProgress(), CancellationToken.None);

        Assert.Null(exitCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RunPostAsync_NonZero_ReportsFailureLine()
    {
        var runner = new FakeProcessRunner { ExitCode = 3 };
        var commandRunner = new PrePostCommandRunner(runner);
        var progress = new RecordingProgress();
        var plan = Plan(kind: OperationKind.Install, postCommand: "echo bye");

        var exitCode = await commandRunner.RunPostAsync(plan, progress, CancellationToken.None);

        Assert.Equal(3, exitCode);
        Assert.Contains("Post-install command failed with exit code 3.", progress.Lines);
    }

    [Fact]
    public void SkippedResult_CarriesExitCodeAndLog()
    {
        string[] log = ["$ echo hi", "Pre-install command failed with exit code 5; skipped."];

        var result = PrePostCommandRunner.SkippedResult(5, log);

        Assert.Equal(5, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(log, result.Log);
    }
}
