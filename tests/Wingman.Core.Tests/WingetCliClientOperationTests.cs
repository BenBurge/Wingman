using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class WingetCliClientOperationTests
{
    // The code winget returned for search-nomatch in exit-codes.txt; any nonzero code would do.
    private const int FailureExitCode = -1978335212;

    private static readonly string[] ScriptedLines =
    [
        "Found Git [Git.Git] Version 2.46.2",
        "Downloading https://github.com/git-for-windows/git/releases/download/v2.46.2.windows.1/Git-2.46.2-64-bit.exe",
        "Successfully verified installer hash",
        "Starting package install...",
        "Successfully installed",
    ];

    private static Task<OperationResult> RunOperation(
        WingetCliClient client, string command, OperationRequest request, IProgress<string> output) => command switch
    {
        "install" => client.InstallAsync(request, output, CancellationToken.None),
        "upgrade" => client.UpgradeAsync(request, output, CancellationToken.None),
        "uninstall" => client.UninstallAsync(request, output, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    [Theory]
    [InlineData("install", 0)]
    [InlineData("install", FailureExitCode)]
    [InlineData("upgrade", 0)]
    [InlineData("upgrade", FailureExitCode)]
    [InlineData("uninstall", 0)]
    [InlineData("uninstall", FailureExitCode)]
    public async Task Operation_StreamsEveryLineAndReportsExitCode(string command, int exitCode)
    {
        var runner = new FakeProcessRunner { StreamedLines = ScriptedLines, ExitCode = exitCode };
        var client = new WingetCliClient(runner);
        var progress = new RecordingProgress();

        var result = await RunOperation(client, command, new OperationRequest("Git.Git"), progress);

        Assert.Equal(ScriptedLines, progress.Lines);
        Assert.Equal(ScriptedLines, result.Log);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(exitCode == 0, result.Succeeded);
        Assert.True(result.Duration >= TimeSpan.Zero);

        var (fileName, arguments) = Assert.Single(runner.Calls);
        Assert.Equal("winget", fileName);
        Assert.Equal([command, "--id", "Git.Git", "--exact"], arguments[..4]);
    }

    [Fact]
    public async Task InstallAsync_PassesRequestOptionsToWinget()
    {
        var runner = new FakeProcessRunner();
        var client = new WingetCliClient(runner);
        var request = new OperationRequest("Git.Git", Version: "2.46.2", Scope: "user");

        await client.InstallAsync(request, new RecordingProgress(), CancellationToken.None);

        var (_, arguments) = Assert.Single(runner.Calls);
        Assert.Equal(WingetArguments.Install(request), arguments);
    }

    [Fact]
    public async Task PinAsync_RunsPinAddAndKeepsLog()
    {
        var runner = new FakeProcessRunner { StreamedLines = ["Pin added successfully."] };
        var client = new WingetCliClient(runner);

        var result = await client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["Pin added successfully."], result.Log);
        var (fileName, arguments) = Assert.Single(runner.Calls);
        Assert.Equal("winget", fileName);
        Assert.Equal(WingetArguments.PinAdd("Git.Git", blocking: true, version: null), arguments);
    }

    [Fact]
    public async Task UnpinAsync_RunsPinRemoveAndReportsFailure()
    {
        var runner = new FakeProcessRunner { StreamedLines = ["No pin found for package: Git.Git"], ExitCode = 1 };
        var client = new WingetCliClient(runner);

        var result = await client.UnpinAsync("Git.Git", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["No pin found for package: Git.Git"], result.Log);
        var (fileName, arguments) = Assert.Single(runner.Calls);
        Assert.Equal("winget", fileName);
        Assert.Equal(WingetArguments.PinRemove("Git.Git"), arguments);
    }
}
