using Wingman.Core.Elevation;
using Wingman.Core.Settings;

namespace Wingman.Core.Tests;

public class PipePeerCheckTests
{
    private const string ExePath = @"C:\Tools\wingman.exe";
    private const int StartedId = 4242;

    [Fact]
    public void IsHelper_Direct_WithTheStartedPid_AcceptsWithoutReadingPathOrSession()
    {
        var reads = 0;

        var isHelper = PipePeerCheck.IsHelper(
            ElevationLauncher.Direct, StartedId, StartedId, ExePath,
            _ => { reads++; return null; },
            _ => { reads++; return false; });

        Assert.True(isHelper);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void IsHelper_Direct_WithAnotherPid_RefusesEvenWhenPathAndSessionMatch()
    {
        var isHelper = PipePeerCheck.IsHelper(
            ElevationLauncher.Direct, StartedId + 1, StartedId, ExePath, _ => ExePath, _ => true);

        Assert.False(isHelper);
    }

    [Fact]
    public void IsHelper_PowerShell_WithTheWrappersChild_AcceptsOnPathAndSession()
    {
        var isHelper = PipePeerCheck.IsHelper(
            ElevationLauncher.PowerShell, StartedId + 1, StartedId, ExePath, _ => @"c:\tools\WINGMAN.EXE", _ => true);

        Assert.True(isHelper);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(@"C:\Temp\wingman.exe", true)]
    [InlineData(ExePath, false)]
    public void IsHelper_PowerShell_WithAnUnreadableOrOtherPathOrAnotherSession_Refuses(string? clientPath, bool inOwnSession)
    {
        var isHelper = PipePeerCheck.IsHelper(
            ElevationLauncher.PowerShell, StartedId + 1, StartedId, ExePath, _ => clientPath, _ => inOwnSession);

        Assert.False(isHelper);
    }

    [Theory]
    [InlineData(ExePath, ExePath, true)]
    [InlineData(ExePath, @"c:\tools\WINGMAN.EXE", true)]
    [InlineData(ExePath, @"C:\Temp\wingman.exe", false)]
    [InlineData(ExePath, null, false)]
    [InlineData(null, ExePath, false)]
    [InlineData("", "", false)]
    public void ImagePathMatches_ComparesPathsIgnoringCaseAndRefusesUnknownOnes(string? expected, string? actual, bool matches)
    {
        Assert.Equal(matches, PipePeerCheck.ImagePathMatches(expected, actual));
    }
}
