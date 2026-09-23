using Wingman.Cli;

namespace Wingman.Core.Tests;

public class CliTrayTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Tray_WithoutARunner_SaysWindowsOnlyAndReturnsUsage()
    {
        var exitCode = await _cli.RunAsync("tray");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal($"wingman tray: Windows only{Environment.NewLine}", _cli.Error.ToString());
    }

    [Fact]
    public async Task Tray_WhenTheRunnerHasNoTray_SaysWindowsOnly()
    {
        _cli.TrayRunner = (_, _) => null;

        var exitCode = await _cli.RunAsync("tray");

        Assert.Equal(ExitCodes.Usage, exitCode);
    }

    [Fact]
    public async Task Tray_RunsWithTheExeAndDataDirectory_AndReturnsItsExitCode()
    {
        (string Exe, string Data)? call = null;
        _cli.TrayRunner = (exe, data) =>
        {
            call = (exe, data);
            return 0;
        };

        var exitCode = await _cli.RunAsync("tray");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal((_cli.ExePath, _cli.DataDirectory), call);
        Assert.Empty(_cli.Error.ToString());
    }
}
