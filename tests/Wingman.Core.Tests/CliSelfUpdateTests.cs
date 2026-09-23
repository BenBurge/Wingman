using Wingman.Cli;
using Wingman.Core.Models;
using Wingman.Core.SelfUpdate;

namespace Wingman.Core.Tests;

public class CliSelfUpdateTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly ScriptedWingetClient _client;
    private readonly RecordingStarter _starter = new();

    public CliSelfUpdateTests()
    {
        _client = new ScriptedWingetClient(_cli.Client) { ShownId = SelfUpdateChecker.PackageId };
        _cli.ClientOverride = _client;
        _cli.SelfUpdateStarter = _starter;
        _cli.Version = "1.0.0";
    }

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Check_WhenNewerIsPublished_SaysSoAndExits10WithoutStarting()
    {
        Publish("9.9.9");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        Assert.Equal($"Wingman 1.0.0; 9.9.9 is available{Environment.NewLine}", _cli.Output);
        Assert.Equal(0, _starter.Calls);
    }

    [Fact]
    public async Task Check_WhenCurrent_SaysUpToDateAndExits0()
    {
        Publish("1.0.0");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"Wingman 1.0.0 is up to date{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task Check_WhenWingetDoesNotKnowWingman_SaysNotInstalledThroughWinget()
    {
        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"Wingman is not installed through winget{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task SelfUpdate_WhenNewerIsPublished_StartsTheDetachedUpgrade()
    {
        Publish("9.9.9");

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(1, _starter.Calls);
        Assert.Equal(
            $"Updating Wingman in the background; restart it when winget finishes.{Environment.NewLine}",
            _cli.Output);
    }

    [Fact]
    public async Task SelfUpdate_WhenCurrent_StartsNothingAndExits0()
    {
        Publish("1.0.0");

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(0, _starter.Calls);
        Assert.Equal($"Wingman 1.0.0 is up to date{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task SelfUpdate_WhenNotInstalledThroughWinget_StartsNothingAndFails()
    {
        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Equal(0, _starter.Calls);
    }

    [Fact]
    public async Task SelfUpdate_WithoutAStarter_SaysWindowsOnly()
    {
        Publish("9.9.9");
        _cli.SelfUpdateStarter = null;

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal($"wingman self-update: Windows only{Environment.NewLine}", _cli.Error.ToString());
    }

    private void Publish(string version) =>
        _client.Shown = new PackageDetails { Id = SelfUpdateChecker.PackageId, Version = version };

    private sealed class RecordingStarter : ISelfUpdateStarter
    {
        public int Calls { get; private set; }

        public void StartDetachedUpgrade() => Calls++;
    }
}
