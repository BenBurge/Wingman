using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class WingetCliClientReadTests
{
    private static readonly string[] CommonFlags = ["--disable-interactivity", "--accept-source-agreements"];

    private static (WingetCliClient Client, FakeProcessRunner Runner) ClientReturning(string fixtureName)
    {
        var runner = new FakeProcessRunner { StandardOutput = Fixtures.Load(fixtureName) };
        return (new WingetCliClient(runner), runner);
    }

    private static void AssertSingleCall(FakeProcessRunner runner, string[] expectedArguments)
    {
        var (fileName, arguments) = Assert.Single(runner.Calls);
        Assert.Equal("winget", fileName);
        Assert.Equal(expectedArguments, arguments);
    }

    [Fact]
    public async Task GetVersionAsync_RunsVersionWithoutCommonFlagsAndTrimsOutput()
    {
        var (client, runner) = ClientReturning("version.txt");

        var version = await client.GetVersionAsync(CancellationToken.None);

        Assert.Equal("v1.29.380", version);
        AssertSingleCall(runner, ["--version"]);
    }

    [Fact]
    public async Task SearchAsync_RunsSearchAndParsesTable()
    {
        var (client, runner) = ClientReturning("search-vscode.txt");

        var rows = await client.SearchAsync("visual studio code", CancellationToken.None);

        Assert.Equal(6, rows.Count);
        AssertSingleCall(runner, ["search", "visual studio code", "--source", "winget", .. CommonFlags]);
    }

    [Fact]
    public async Task ListInstalledAsync_RunsListAndParsesTable()
    {
        var (client, runner) = ClientReturning("list.txt");

        var rows = await client.ListInstalledAsync(CancellationToken.None);

        Assert.Equal(212, rows.Count);
        AssertSingleCall(runner, ["list", .. CommonFlags]);
        Assert.DoesNotContain("--source", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ListUpgradesAsync_RunsUpgradeIncludeUnknownAndPinnedAndParsesTable()
    {
        var (client, runner) = ClientReturning("upgrade-include-unknown.txt");

        var rows = await client.ListUpgradesAsync(CancellationToken.None);

        Assert.Equal(17, rows.Count);
        AssertSingleCall(runner, ["upgrade", "--include-unknown", "--include-pinned", .. CommonFlags]);
    }

    [Fact]
    public async Task ShowAsync_RunsExactShowAndParsesDetails()
    {
        var (client, runner) = ClientReturning("show-vscode.txt");

        var details = await client.ShowAsync("Microsoft.VisualStudioCode", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Microsoft.VisualStudioCode", details.Id);
        AssertSingleCall(runner, ["show", "--id", "Microsoft.VisualStudioCode", "--exact", "--source", "winget", .. CommonFlags]);
    }

    [Fact]
    public async Task ShowAsync_NoMatch_ReturnsNull()
    {
        var runner = new FakeProcessRunner { StandardOutput = "No package found matching input criteria.\r\n" };
        var client = new WingetCliClient(runner);

        var details = await client.ShowAsync("Vendor.Missing", CancellationToken.None);

        Assert.Null(details);
    }

    [Fact]
    public async Task ListVersionsAsync_RunsExactShowVersionsAndParsesVersions()
    {
        var (client, runner) = ClientReturning("show-versions-git.txt");

        var versions = await client.ListVersionsAsync("Git.Git", CancellationToken.None);

        Assert.Equal("2.55.0.3", versions[0]);
        AssertSingleCall(runner, ["show", "--id", "Git.Git", "--exact", "--source", "winget", "--versions", .. CommonFlags]);
    }

    [Fact]
    public async Task ListPinsAsync_RunsPinListAndParsesPins()
    {
        var (client, runner) = ClientReturning("pin-list.txt");

        IReadOnlyList<Pin> pins = await client.ListPinsAsync(CancellationToken.None);

        Assert.Empty(pins);
        AssertSingleCall(runner, ["pin", "list", .. CommonFlags]);
        Assert.DoesNotContain("--source", runner.Calls[0].Arguments);
    }
}
