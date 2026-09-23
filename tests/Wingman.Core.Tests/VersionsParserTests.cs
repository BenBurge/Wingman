using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class VersionsParserTests
{
    [Fact]
    public void Parse_ShowVersionsGitFixture_ReturnsEveryVersionNewestFirst()
    {
        var versions = WingetVersionsParser.Parse(Fixtures.Load("show-versions-git.txt"));

        Assert.Equal(73, versions.Count);
        Assert.Equal("2.55.0.3", versions[0]);
        Assert.Equal("2.24.1.2", versions[^1]);
    }
}
