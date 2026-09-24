using System.Net;
using Wingman.Core.SelfUpdate;
using Wingman.Core.State;

namespace Wingman.Core.Tests;

public class SelfUpdateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeGitHub _gitHub = new();
    private readonly HttpClient _http;
    private readonly GitHubReleaseSource _source;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));
    private readonly StateStore _state;

    public SelfUpdateTests()
    {
        _http = new HttpClient(_gitHub);
        _source = new GitHubReleaseSource(_http);
        _state = new StateStore(_directory);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // --- GitHubReleaseSource.GetLatestAsync ---

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public async Task GetLatest_PicksTheInstallerAndHashForTheRuntime(string rid)
    {
        _gitHub.Publish("1.2.0");

        var release = await _source.GetLatestAsync(rid, CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal("1.2.0", release.Version);
        Assert.Equal("v1.2.0", release.TagName);
        var setupName = $"wingman-v1.2.0-{rid}-setup.exe";
        Assert.Equal(FakeGitHub.DownloadUrl("1.2.0", setupName), release.SetupUrl.AbsoluteUri);
        Assert.Equal(FakeGitHub.DownloadUrl("1.2.0", setupName + ".sha256"), release.Sha256Url.AbsoluteUri);
        Assert.Equal("https://github.com/BenBurge/Wingman/releases/tag/v1.2.0", release.ReleaseNotesUrl);
    }

    [Fact]
    public async Task GetLatest_SendsTheUserAgentAndGitHubAcceptHeader()
    {
        _gitHub.Publish("1.2.0");

        await _source.GetLatestAsync("win-x64", CancellationToken.None);

        var request = Assert.Single(_gitHub.Requests);
        Assert.Equal(FakeGitHub.LatestUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("Wingman", Assert.Single(request.Headers.UserAgent).Product!.Name);
        Assert.Equal("application/vnd.github+json", Assert.Single(request.Headers.Accept).MediaType);
    }

    [Fact]
    public async Task GetLatest_IgnoresSimilarlyNamedAssets()
    {
        _gitHub.ServeLatest(FakeGitHub.ReleaseJson(
            "v1.2.0",
            "wingman-v1.2.0-win-x64.zip",
            "wingman-v1.2.0-win-x64.zip.sha256",
            "wingman-v1.2.0-win-x64-setup.exe.sig",
            "wingman-v1.2.0-win-x64-setup.exe",
            "wingman-v1.2.0-win-x64-setup.exe.sha256"));

        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);

        Assert.NotNull(release);
        Assert.EndsWith("/wingman-v1.2.0-win-x64-setup.exe", release.SetupUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("/wingman-v1.2.0-win-x64-setup.exe.sha256", release.Sha256Url.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wingman-v1.2.0-win-x64.zip", "wingman-v1.2.0-win-x64.zip.sha256")]
    [InlineData("wingman-v1.2.0-win-x64-setup.exe")]
    [InlineData("wingman-v1.2.0-win-x64-setup.exe.sha256")]
    [InlineData("wingman-v1.2.0-win-arm64-setup.exe", "wingman-v1.2.0-win-arm64-setup.exe.sha256")]
    [InlineData]
    public async Task GetLatest_WithoutBothAssetsForTheRuntime_ReturnsNull(params string[] assetNames)
    {
        _gitHub.ServeLatest(FakeGitHub.ReleaseJson("v1.2.0", assetNames));

        Assert.Null(await _source.GetLatestAsync("win-x64", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatest_WhenGitHubRefuses_ReturnsNull(HttpStatusCode status)
    {
        _gitHub.ServeLatestStatus(status);

        Assert.Null(await _source.GetLatestAsync("win-x64", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatest_WhenTheNetworkFails_ReturnsNull()
    {
        _gitHub.Failure = new HttpRequestException("No such host is known.");

        Assert.Null(await _source.GetLatestAsync("win-x64", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatest_WhenTheRequestTimesOut_ReturnsNull()
    {
        _gitHub.Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        Assert.Null(await _source.GetLatestAsync("win-x64", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatest_WithMalformedJson_ReturnsNull()
    {
        _gitHub.ServeLatest("{ not json");

        Assert.Null(await _source.GetLatestAsync("win-x64", CancellationToken.None));
    }

    [Fact]
    public async Task GetLatest_WhenTheCallerCancels_Throws()
    {
        _gitHub.Publish("1.2.0");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _source.GetLatestAsync("win-x64", cancel.Token));
    }

    // --- SelfUpdateChecker.CheckAsync ---

    [Theory]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.3.0", "1.2.0", false)]
    [InlineData("v1.1.0", "1.2.0", true)]
    [InlineData("1.9.0", "1.10.0", true)]
    [InlineData("0.0.0-dev", "1.2.0", false)]
    [InlineData("0.0.0", "1.2.0", false)]
    public async Task Check_ComparesTheInstalledVersionWithTheRelease(string installed, string published, bool isNewer)
    {
        _gitHub.Publish(published);

        var check = await CheckAsync(installed);

        Assert.Equal(isNewer, check.IsNewerAvailable);
        Assert.Equal(published, check.Latest?.Version);
        Assert.Equal(installed, check.InstalledVersion);
    }

    [Fact]
    public async Task Check_APrereleaseTag_IsNeverNewer()
    {
        _gitHub.Publish("2.0.0-beta.1");

        var check = await CheckAsync("1.0.0");

        Assert.False(check.IsNewerAvailable);
        Assert.Equal("2.0.0-beta.1", check.Latest?.Version);
    }

    [Fact]
    public async Task Check_WhenGitHubFailsWithNothingCached_HasNoLatest()
    {
        _gitHub.Failure = new HttpRequestException("offline");

        var check = await CheckAsync("1.0.0");

        Assert.Null(check.Latest);
        Assert.False(check.IsNewerAvailable);
    }

    [Fact]
    public async Task Check_Stale_AsksGitHubAndStoresTheRelease()
    {
        _gitHub.Publish("1.2.0");
        _state.Update(state => state.LastUpdateCheck = Now - TimeSpan.FromHours(7));

        var check = await CheckAsync("1.0.0");

        Assert.Equal(1, _gitHub.LatestRequests);
        Assert.True(check.IsNewerAvailable);
        var saved = _state.Load();
        Assert.Equal(Now, saved.LastUpdateCheck);
        Assert.Equal("1.2.0", saved.LatestVersion);
        Assert.Equal("v1.2.0", saved.LatestTag);
        Assert.Equal(FakeGitHub.DownloadUrl("1.2.0", "wingman-v1.2.0-win-x64-setup.exe"), saved.LatestSetupUrl);
        Assert.Equal(FakeGitHub.DownloadUrl("1.2.0", "wingman-v1.2.0-win-x64-setup.exe.sha256"), saved.LatestSha256Url);
    }

    [Fact]
    public async Task Check_Fresh_UsesTheCacheWithoutAnyRequest()
    {
        _gitHub.Publish("1.2.0");
        await CheckAsync("1.0.0");
        _gitHub.Requests.Clear();

        var check = await SelfUpdateChecker.CheckAsync(
            _source, "1.0.0", "win-x64", _state, SelfUpdateChecker.MaxAge, Now + TimeSpan.FromHours(5), CancellationToken.None);

        Assert.Empty(_gitHub.Requests);
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("1.2.0", check.Latest?.Version);
        Assert.Equal("https://github.com/BenBurge/Wingman/releases/tag/v1.2.0", check.Latest?.ReleaseNotesUrl);
    }

    [Fact]
    public async Task Check_WhenGitHubFails_FallsBackToAStaleCache()
    {
        _gitHub.Publish("1.2.0");
        await CheckAsync("1.0.0");
        _gitHub.Failure = new HttpRequestException("offline");

        var check = await SelfUpdateChecker.CheckAsync(
            _source, "1.0.0", "win-x64", _state, SelfUpdateChecker.MaxAge, Now + TimeSpan.FromDays(2), CancellationToken.None);

        Assert.Equal("1.2.0", check.Latest?.Version);
        Assert.True(check.IsNewerAvailable);
        Assert.Equal(Now, _state.Load().LastUpdateCheck);
    }

    [Fact]
    public async Task Check_ACheckStampedInTheFuture_IsStale()
    {
        _gitHub.Publish("1.2.0");
        _state.Update(state =>
        {
            state.LastUpdateCheck = Now + TimeSpan.FromHours(1);
            state.LatestVersion = "1.1.0";
            state.LatestTag = "v1.1.0";
            state.LatestSetupUrl = FakeGitHub.DownloadUrl("1.1.0", "wingman-v1.1.0-win-x64-setup.exe");
            state.LatestSha256Url = FakeGitHub.DownloadUrl("1.1.0", "wingman-v1.1.0-win-x64-setup.exe.sha256");
        });

        var check = await CheckAsync("1.0.0");

        Assert.Equal(1, _gitHub.LatestRequests);
        Assert.Equal("1.2.0", check.Latest?.Version);
    }

    // --- SelfUpdateChecker.NormalizeVersion ---

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("1.2.3-dev", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("1.2.3+build5", "1.2.3")]
    [InlineData(" v1.2.3 ", "1.2.3")]
    public void NormalizeVersion_StripsPrefixAndSuffix(string version, string expected)
    {
        Assert.Equal(expected, SelfUpdateChecker.NormalizeVersion(version));
    }

    // --- UpdateDownloader.DownloadVerifiedAsync ---

    [Fact]
    public async Task Download_WhenTheHashMatches_ReturnsTheInstallerUnderItsOwnName()
    {
        _gitHub.Publish("1.2.0");
        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);

        var path = await new UpdateDownloader(_http).DownloadVerifiedAsync(release!, _directory, CancellationToken.None);

        Assert.Equal(Path.Combine(_directory, "wingman-v1.2.0-win-x64-setup.exe"), path);
        Assert.Equal(FakeGitHub.SetupBytes("1.2.0"), File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task Download_AcceptsAnUppercaseHash()
    {
        _gitHub.Publish("1.2.0");
        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(FakeGitHub.SetupBytes("1.2.0")));
        _gitHub.Serve(release!.Sha256Url.AbsoluteUri, () => FakeGitHub.Text($"{hash}  wingman-v1.2.0-win-x64-setup.exe\r\n"));

        var path = await new UpdateDownloader(_http).DownloadVerifiedAsync(release, _directory, CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Download_WhenTheHashDoesNotMatch_DeletesTheFileAndThrows()
    {
        _gitHub.Publish("1.2.0");
        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);
        _gitHub.Serve(release!.SetupUrl.AbsoluteUri, () => FakeGitHub.Bytes([1, 2, 3]));

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateDownloader(_http).DownloadVerifiedAsync(release, _directory, CancellationToken.None));

        Assert.Equal("Downloaded installer does not match its published SHA-256", error.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_WithAMalformedHashFile_ThrowsBeforeDownloadingTheInstaller()
    {
        _gitHub.Publish("1.2.0");
        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);
        _gitHub.Serve(release!.Sha256Url.AbsoluteUri, () => FakeGitHub.Text("not-a-hash  wingman-v1.2.0-win-x64-setup.exe"));
        _gitHub.Requests.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateDownloader(_http).DownloadVerifiedAsync(release, _directory, CancellationToken.None));

        Assert.DoesNotContain(_gitHub.Requests, request => request.RequestUri == release.SetupUrl);
    }

    [Fact]
    public async Task Download_WhenTheInstallerIsMissing_Throws()
    {
        _gitHub.Publish("1.2.0");
        var release = await _source.GetLatestAsync("win-x64", CancellationToken.None);
        _gitHub.Serve(release!.SetupUrl.AbsoluteUri, () => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new UpdateDownloader(_http).DownloadVerifiedAsync(release, _directory, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    // --- InstallDetector.Detect ---

    [Theory]
    [InlineData(@"C:\Users\ben\AppData\Local\Programs\Wingman\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman\", InstallKind.Installer)]
    [InlineData(@"C:\Users\ben\AppData\Local\Programs\Wingman\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman", InstallKind.Installer)]
    [InlineData(@"c:\users\BEN\appdata\local\programs\wingman\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman\", InstallKind.Installer)]
    [InlineData(@"C:\Tools\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman\", InstallKind.Portable)]
    [InlineData(@"C:\Users\ben\AppData\Local\Programs\Wingman\old\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman\", InstallKind.Portable)]
    [InlineData(@"C:\Users\ben\AppData\Local\Programs\Wingman2\wingman.exe", @"C:\Users\ben\AppData\Local\Programs\Wingman", InstallKind.Portable)]
    [InlineData(@"C:\Tools\wingman.exe", null, InstallKind.Portable)]
    [InlineData(@"C:\Tools\wingman.exe", "", InstallKind.Portable)]
    [InlineData("wingman", @"C:\Tools", InstallKind.Portable)]
    public void Detect_IsInstallerOnlyInTheRegisteredFolder(string exePath, string? registeredFolder, InstallKind expected)
    {
        Assert.Equal(expected, InstallDetector.Detect(exePath, registeredFolder));
    }

    private Task<UpdateCheck> CheckAsync(string installed) =>
        SelfUpdateChecker.CheckAsync(_source, installed, "win-x64", _state, SelfUpdateChecker.MaxAge, Now, CancellationToken.None);
}
