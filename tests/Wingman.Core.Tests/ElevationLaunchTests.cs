using Wingman.Core.Elevation;
using Wingman.Core.Settings;

namespace Wingman.Core.Tests;

public class ElevationLaunchTests
{
    private const string SystemDirectory = @"C:\Windows\System32";
    private const string PowerShellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string ExePath = @"C:\Users\O'Brien\My Apps\wingman.exe";

    private static readonly string[] WorkerArgs = ["--elevated-worker", "wingman-elevated-abc", "--parent", "4242"];

    [Fact]
    public void Build_Direct_StartsTheExeWithItsArguments()
    {
        var (fileName, arguments) = ElevationLaunch.Build(ElevationLauncher.Direct, SystemDirectory, ExePath, WorkerArgs, hidden: true);

        Assert.Equal(ExePath, fileName);
        Assert.Equal("--elevated-worker wingman-elevated-abc --parent 4242", arguments);
    }

    [Theory]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\no-spaces\", @"C:\no-spaces\")]
    [InlineData(@"C:\with space\", "\"C:\\with space\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Build_Direct_QuotesArgumentsForTheWindowsCommandLine(string arg, string expected)
    {
        var (_, arguments) = ElevationLaunch.Build(ElevationLauncher.Direct, SystemDirectory, ExePath, [arg], hidden: false);

        Assert.Equal(expected, arguments);
    }

    [Fact]
    public void Build_PowerShellHidden_RunsTheExeFromSystemPowerShellInAHiddenWindow()
    {
        var (fileName, arguments) = ElevationLaunch.Build(ElevationLauncher.PowerShell, SystemDirectory, ExePath, WorkerArgs, hidden: true);

        Assert.Equal(PowerShellPath, fileName);
        Assert.Equal(
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command " +
            @"""& 'C:\Users\O''Brien\My Apps\wingman.exe' '--elevated-worker' 'wingman-elevated-abc' '--parent' '4242'""",
            arguments);
    }

    [Fact]
    public void Build_PowerShellVisible_LeavesTheWindowStyleAlone()
    {
        var (fileName, arguments) = ElevationLaunch.Build(ElevationLauncher.PowerShell, SystemDirectory, ExePath, ["--fake"], hidden: false);

        Assert.Equal(PowerShellPath, fileName);
        Assert.Equal(
            @"-NoProfile -ExecutionPolicy Bypass -Command ""& 'C:\Users\O''Brien\My Apps\wingman.exe' '--fake'""",
            arguments);
    }

    [Fact]
    public void Build_PowerShell_DoublesApostrophesInArguments()
    {
        var (_, arguments) = ElevationLaunch.Build(ElevationLauncher.PowerShell, SystemDirectory, @"C:\wingman.exe", ["it's", "a b"], hidden: false);

        Assert.Equal(@"-NoProfile -ExecutionPolicy Bypass -Command ""& 'C:\wingman.exe' 'it''s' 'a b'""", arguments);
    }

    [Fact]
    public void Build_PowerShell_WithNoArguments_RunsTheExeAlone()
    {
        var (_, arguments) = ElevationLaunch.Build(ElevationLauncher.PowerShell, SystemDirectory, @"C:\wingman.exe", [], hidden: false);

        Assert.Equal(@"-NoProfile -ExecutionPolicy Bypass -Command ""& 'C:\wingman.exe'""", arguments);
    }
}
