using System.Net;
using Wingman.Cli;
using Wingman.Core.SelfUpdate;

namespace Wingman.Core.Tests;

public class CliSelfUpdateTests : IDisposable
{
    private const string InstallFolder = @"C:\Users\ben\AppData\Local\Programs\Wingman";

    private readonly CliHarness _cli = new();
    private readonly FakeGitHub _gitHub = new();
    private readonly HttpClient _http;
    private readonly RecordingStarter _starter = new();

    public CliSelfUpdateTests()
    {
        _http = new HttpClient(_gitHub);
        _cli.ReleaseSource = new GitHubReleaseSource(_http);
        _cli.UpdateDownloader = new UpdateDownloader(_http);
        _cli.SelfUpdateStarter = _starter;
        _cli.InstallerRegisteredFolder = InstallFolder + @"\";
        _cli.ExePath = InstallFolder + @"\wingman.exe";
        _cli.Version = "1.0.0";
    }

    public void Dispose()
    {
        _http.Dispose();
        _cli.Dispose();
    }

    // --- self-update --check ---

    [Fact]
    public async Task Check_WhenNewerIsPublished_SaysSoAndExits10WithoutStarting()
    {
        _gitHub.Publish("9.9.9");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        Assert.Equal($"Wingman 1.0.0; 9.9.9 is available{Environment.NewLine}", _cli.Output);
        Assert.Empty(_starter.SetupPaths);
    }

    [Fact]
    public async Task Check_WhenCurrent_SaysUpToDateAndExits0()
    {
        _gitHub.Publish("1.0.0");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"Wingman 1.0.0 is up to date{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task Check_WhenGitHubCannotBeReachedAndNothingIsCached_Exits1()
    {
        _gitHub.Failure = new HttpRequestException("offline");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Equal($"Could not reach GitHub Releases{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task Check_WhenGitHubHasNoRelease_Exits1()
    {
        _gitHub.ServeLatestStatus(HttpStatusCode.NotFound);

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Equal($"Could not reach GitHub Releases{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task Check_AlwaysAsksGitHub_AndFallsBackToTheCacheWhenItFails()
    {
        _gitHub.Publish("9.9.9");
        await _cli.RunAsync("self-update", "--check");
        _gitHub.Failure = new HttpRequestException("offline");

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(2, _gitHub.LatestRequests);
        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
    }

    // --- self-update ---

    [Fact]
    public async Task SelfUpdate_WhenNewer_DownloadsVerifiesAndStartsTheInstaller()
    {
        _gitHub.Publish("9.9.9");

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Success, exitCode);
        var setupPath = Assert.Single(_starter.SetupPaths);
        Assert.Equal(Path.Combine(_cli.UpdateDirectory, "wingman-v9.9.9-win-x64-setup.exe"), setupPath);
        Assert.Equal(FakeGitHub.SetupBytes("9.9.9"), File.ReadAllBytes(setupPath));
        Assert.Equal($"Installing Wingman 9.9.9; it restarts itself when done.{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task SelfUpdate_OnArm64_DownloadsTheArm64Installer()
    {
        _gitHub.Publish("9.9.9");
        _cli.Rid = "win-arm64";

        await _cli.RunAsync("self-update");

        Assert.EndsWith("wingman-v9.9.9-win-arm64-setup.exe", Assert.Single(_starter.SetupPaths), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelfUpdate_WhenTheHashDoesNotMatch_StartsNothingAndFails()
    {
        _gitHub.Publish("9.9.9");
        _gitHub.Serve(FakeGitHub.DownloadUrl("9.9.9", "wingman-v9.9.9-win-x64-setup.exe"), () => FakeGitHub.Bytes([0]));

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Empty(_starter.SetupPaths);
        Assert.Contains("Downloaded installer does not match its published SHA-256", _cli.Error.ToString());
    }

    [Fact]
    public async Task SelfUpdate_WhenCurrent_StartsNothingAndExits0()
    {
        _gitHub.Publish("1.0.0");

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_starter.SetupPaths);
        Assert.Equal($"Wingman 1.0.0 is up to date{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task SelfUpdate_FromAPortableCopy_PointsAtTheReleaseAndInstallsNothing()
    {
        _gitHub.Publish("9.9.9");
        _cli.ExePath = @"C:\Tools\wingman.exe";

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(_starter.SetupPaths);
        Assert.Equal(
            [
                "Wingman 9.9.9 is available: https://github.com/BenBurge/Wingman/releases/tag/v9.9.9",
                "Run the installer to switch this copy to an installed one",
            ],
            _cli.OutputLines);
    }

    [Fact]
    public async Task SelfUpdate_FromAPortableCopyWithYes_Installs()
    {
        _gitHub.Publish("9.9.9");
        _cli.ExePath = @"C:\Tools\wingman.exe";
        _cli.InstallerRegisteredFolder = null;

        var exitCode = await _cli.RunAsync("self-update", "--yes");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Single(_starter.SetupPaths);
    }

    [Fact]
    public async Task SelfUpdate_WithoutAStarter_SaysWindowsOnly()
    {
        _gitHub.Publish("9.9.9");
        _cli.SelfUpdateStarter = null;

        var exitCode = await _cli.RunAsync("self-update");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal($"wingman self-update: Windows only{Environment.NewLine}", _cli.Error.ToString());
    }

    [Fact]
    public async Task SelfUpdate_WithoutAReleaseSource_Refuses()
    {
        _cli.ReleaseSource = null;

        var exitCode = await _cli.RunAsync("self-update", "--check");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Empty(_gitHub.Requests);
    }

    // --- check --notify ---

    [Fact]
    public async Task CheckNotify_WhenInstalledAutoUpdatingAndNewer_StartsTheInstallerAndMarksItPending()
    {
        _gitHub.Publish("9.9.9");

        var exitCode = await _cli.RunAsync("check", "--notify");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        Assert.Equal(Path.Combine(_cli.UpdateDirectory, "wingman-v9.9.9-win-x64-setup.exe"), Assert.Single(_starter.SetupPaths));
        Assert.Equal("9.9.9", _cli.State.Load().PendingUpdateVersion);
    }

    [Fact]
    public async Task CheckNotify_WithAutoUpdateOff_AsksNothingAndStartsNothing()
    {
        _gitHub.Publish("9.9.9");
        _cli.Settings.AutoUpdateWingman = false;

        await _cli.RunAsync("check", "--notify");

        Assert.Empty(_gitHub.Requests);
        Assert.Empty(_starter.SetupPaths);
    }

    [Fact]
    public async Task CheckNotify_FromAPortableCopy_StartsNothing()
    {
        _gitHub.Publish("9.9.9");
        _cli.ExePath = @"C:\Tools\wingman.exe";

        await _cli.RunAsync("check", "--notify");

        Assert.Empty(_starter.SetupPaths);
        Assert.Equal("", _cli.State.Load().PendingUpdateVersion);
    }

    [Fact]
    public async Task CheckNotify_WhenNothingNewer_StartsNothing()
    {
        _gitHub.Publish("1.0.0");

        await _cli.RunAsync("check", "--notify");

        Assert.Empty(_starter.SetupPaths);
    }

    [Fact]
    public async Task Check_WithoutNotify_NeverUpdatesWingman()
    {
        _gitHub.Publish("9.9.9");

        await _cli.RunAsync("check");

        Assert.Empty(_gitHub.Requests);
        Assert.Empty(_starter.SetupPaths);
    }

    [Fact]
    public async Task CheckNotify_WhenTheDownloadFails_RecordsTheErrorAndStillSucceeds()
    {
        _gitHub.Publish("9.9.9");
        _gitHub.Serve(FakeGitHub.DownloadUrl("9.9.9", "wingman-v9.9.9-win-x64-setup.exe"), () => FakeGitHub.Bytes([0]));

        var exitCode = await _cli.RunAsync("check", "--notify");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        Assert.Empty(_starter.SetupPaths);
        var state = _cli.State.Load();
        Assert.Equal("Could not update Wingman to 9.9.9: Downloaded installer does not match its published SHA-256", state.LastError);
        Assert.Equal("", state.PendingUpdateVersion);
    }

    [Fact]
    public async Task CheckNotify_WhenGitHubIsUnreachable_StillSucceeds()
    {
        _gitHub.Failure = new HttpRequestException("offline");

        var exitCode = await _cli.RunAsync("check", "--notify");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        Assert.Empty(_starter.SetupPaths);
    }

    [Fact]
    public async Task CheckNotify_UsesTheCachedReleaseWithinSixHours()
    {
        _gitHub.Publish("1.0.0");
        await _cli.RunAsync("check", "--notify");
        _cli.Now += TimeSpan.FromHours(1);

        await _cli.RunAsync("check", "--notify");

        Assert.Equal(1, _gitHub.LatestRequests);
    }

    // --- the toast after an update ---

    [Fact]
    public async Task CheckNotify_AsTheNewVersion_AnnouncesTheUpdateOnce()
    {
        _gitHub.Publish("9.9.9");
        await _cli.RunAsync("check", "--notify");
        _cli.Version = "9.9.9";
        _cli.Toasts.Clear();

        await _cli.RunAsync("check", "--notify");
        await _cli.RunAsync("check", "--notify");

        var announced = Assert.Single(_cli.Toasts, toast => toast.Tag == "wingman-self-update");
        Assert.Equal("Wingman updated to v9.9.9", announced.Title);
        Assert.Equal("", _cli.State.Load().PendingUpdateVersion);
        Assert.Single(_starter.SetupPaths);
    }

    [Fact]
    public async Task CheckNotify_BeforeTheNewVersionRuns_DoesNotAnnounce()
    {
        _cli.State.Update(state => state.PendingUpdateVersion = "9.9.9");

        await _cli.RunAsync("check", "--notify");

        Assert.DoesNotContain(_cli.Toasts, toast => toast.Tag == "wingman-self-update");
        Assert.Equal("9.9.9", _cli.State.Load().PendingUpdateVersion);
    }

    [Fact]
    public async Task CheckNotify_WhilePaused_ClearsThePendingUpdateWithoutAToast()
    {
        _cli.State.Update(state => state.PendingUpdateVersion = "1.0.0");
        _cli.Settings.NotificationsPaused = true;

        await _cli.RunAsync("check", "--notify");

        Assert.Empty(_cli.Toasts);
        Assert.Equal("", _cli.State.Load().PendingUpdateVersion);
    }

    private sealed class RecordingStarter : ISelfUpdateStarter
    {
        public List<string> SetupPaths { get; } = [];

        public void StartInstaller(string setupPath) => SetupPaths.Add(setupPath);
    }
}
