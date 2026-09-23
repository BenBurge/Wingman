using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class FakeWingetClientTests
{
    private static FakeWingetClient CreateClient() => new(TimeSpan.Zero);

    [Fact]
    public async Task ReadState_MatchesTheFixturesItWasBuiltFrom()
    {
        var client = CreateClient();

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        var upgrades = await client.ListUpgradesAsync(CancellationToken.None);
        var version = await client.GetVersionAsync(CancellationToken.None);

        Assert.Equal(212, installed.Count);
        Assert.Equal(17, upgrades.Count);
        Assert.Equal("v1.29.380", version);
    }

    [Fact]
    public async Task SearchAsync_MatchesNameOrIdCaseInsensitively()
    {
        var client = CreateClient();

        var gitResults = await client.SearchAsync("git", CancellationToken.None);
        var upperCaseResults = await client.SearchAsync("GIT", CancellationToken.None);
        var noResults = await client.SearchAsync("zzzz", CancellationToken.None);

        Assert.Contains(gitResults, row => row.Id == "Git.Git");
        Assert.Contains(upperCaseResults, row => row.Id == "Git.Git");
        Assert.Empty(noResults);
    }

    [Fact]
    public async Task SearchAsync_VisualStudioCode_DropsMsstoreRowsButKeepsWinget()
    {
        var client = CreateClient();

        var results = await client.SearchAsync("visual studio code", CancellationToken.None);

        Assert.DoesNotContain(results, row => string.Equals(row.Source, "msstore", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, row => row.Id == "Microsoft.VisualStudioCode");
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var client = CreateClient();

        var results = await client.SearchAsync("", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ShowAsync_VsCode_ReturnsCapturedDetails()
    {
        var client = CreateClient();

        var details = await client.ShowAsync("Microsoft.VisualStudioCode", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Microsoft Corporation", details.Publisher);
    }

    [Fact]
    public async Task ShowAsync_UnknownId_ReturnsNull()
    {
        var client = CreateClient();

        var details = await client.ShowAsync("Nonexistent.Package", CancellationToken.None);

        Assert.Null(details);
    }

    [Fact]
    public async Task ShowAsync_IdNotCapturedByWingetShow_SynthesizesDetailsFromItsRow()
    {
        var client = CreateClient();

        var details = await client.ShowAsync("AutoHotkey.AutoHotkey", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("AutoHotkey", details.Name);
        Assert.Equal("AutoHotkey", details.Publisher);
        Assert.Equal("Unknown", details.License);
    }

    [Fact]
    public async Task ListVersionsAsync_Git_StartsWithNewestCapturedVersion()
    {
        var client = CreateClient();

        var versions = await client.ListVersionsAsync("Git.Git", CancellationToken.None);

        Assert.Equal("2.55.0.3", versions[0]);
    }

    [Fact]
    public async Task InstallAsync_KnownIdNotAlreadyInstalled_StreamsAndAddsToInstalled()
    {
        var client = CreateClient();
        var progress = new RecordingProgress();

        var result = await client.InstallAsync(new OperationRequest("Microsoft.Git"), progress, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "$ winget install --id Microsoft.Git --exact --disable-interactivity --accept-source-agreements",
            progress.Lines[0]);
        Assert.Equal("Successfully installed", progress.Lines[^1]);

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        Assert.Contains(installed, row => row.Id == "Microsoft.Git");
    }

    [Fact]
    public async Task InstallAsync_IdContainingFail_FailsWithoutChangingInstalledState()
    {
        var client = CreateClient();

        var result = await client.InstallAsync(new OperationRequest("Vendor.WillFail"), new RecordingProgress(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1603, result.ExitCode);

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        Assert.DoesNotContain(installed, row => row.Id == "Vendor.WillFail");
    }

    [Fact]
    public async Task UpgradeAsync_MovesInstalledRowToItsAvailableVersionAndClearsTheUpgrade()
    {
        var client = CreateClient();

        var result = await client.UpgradeAsync(new OperationRequest("AutoHotkey.AutoHotkey"), new RecordingProgress(), CancellationToken.None);

        Assert.True(result.Succeeded);

        var upgrades = await client.ListUpgradesAsync(CancellationToken.None);
        Assert.DoesNotContain(upgrades, row => row.Id == "AutoHotkey.AutoHotkey");

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        var row = Assert.Single(installed, r => r.Id == "AutoHotkey.AutoHotkey");
        Assert.Equal("2.0.28", row.Version);
        Assert.Null(row.AvailableVersion);
    }

    [Fact]
    public async Task UpgradeAsync_ExplicitVersionBelowAvailable_InstallsItAndKeepsTheUpgradeRow()
    {
        var client = CreateClient();

        var result = await client.UpgradeAsync(
            new OperationRequest("AutoHotkey.AutoHotkey", Version: "2.0.27"),
            new RecordingProgress(),
            CancellationToken.None);

        Assert.True(result.Succeeded);

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        var installedRow = Assert.Single(installed, r => r.Id == "AutoHotkey.AutoHotkey");
        Assert.Equal("2.0.27", installedRow.Version);

        var upgrades = await client.ListUpgradesAsync(CancellationToken.None);
        var upgradeRow = Assert.Single(upgrades, r => r.Id == "AutoHotkey.AutoHotkey");
        Assert.Equal("2.0.27", upgradeRow.Version);
        Assert.Equal("2.0.28", upgradeRow.AvailableVersion);
    }

    [Fact]
    public async Task UpgradeAsync_ExplicitVersion_StreamsThatVersionInTheFoundLine()
    {
        var client = CreateClient();
        var progress = new RecordingProgress();

        await client.UpgradeAsync(
            new OperationRequest("AutoHotkey.AutoHotkey", Version: "2.0.27"),
            progress,
            CancellationToken.None);

        Assert.Contains("Found AutoHotkey [AutoHotkey.AutoHotkey] Version 2.0.27", progress.Lines);
    }

    [Fact]
    public async Task UninstallAsync_RemovesTheRowFromInstalled()
    {
        var client = CreateClient();

        var result = await client.UninstallAsync(new OperationRequest("AutoHotkey.AutoHotkey"), new RecordingProgress(), CancellationToken.None);

        Assert.True(result.Succeeded);

        var installed = await client.ListInstalledAsync(CancellationToken.None);
        Assert.DoesNotContain(installed, row => row.Id == "AutoHotkey.AutoHotkey");
    }

    [Fact]
    public async Task UpgradeAsync_UnknownId_ReturnsWingetsNoMatchExitCode()
    {
        var client = CreateClient();

        var result = await client.UpgradeAsync(new OperationRequest("Nonexistent.Package"), new RecordingProgress(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(-1978335212, result.ExitCode);
        Assert.Equal(["No installed package found matching input criteria."], result.Log);
    }

    [Fact]
    public async Task PinAsync_ThenListPinsAsync_ShowsOneBlockingPin_AndUnpinClearsIt()
    {
        var client = CreateClient();

        await client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);
        var pinsAfterPin = await client.ListPinsAsync(CancellationToken.None);

        var pin = Assert.Single(pinsAfterPin);
        Assert.Equal("Git.Git", pin.Id);
        Assert.Equal(PinType.Blocking, pin.PinType);

        await client.UnpinAsync("Git.Git", CancellationToken.None);
        var pinsAfterUnpin = await client.ListPinsAsync(CancellationToken.None);

        Assert.Empty(pinsAfterUnpin);
    }

    [Fact]
    public async Task InstallAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var client = new FakeWingetClient(TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.InstallAsync(new OperationRequest("Microsoft.Git"), new RecordingProgress(), cts.Token));
    }
}
