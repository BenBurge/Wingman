using Wingman.Core.Models;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class SelfUpdateTests
{
    // --- SelfUpdateChecker.CheckAsync ---

    [Fact]
    public async Task CheckAsync_UnknownPackage_ReturnsNotNewerAndNullAvailable()
    {
        var client = new FakeWingetClient(TimeSpan.Zero);

        var result = await SelfUpdateChecker.CheckAsync(client, "1.0.0", CancellationToken.None);

        Assert.False(result.IsNewerAvailable);
        Assert.Null(result.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_InstalledOlderThanAvailable_ReturnsNewer()
    {
        var client = new StubWingetClient("1.2.0");

        var result = await SelfUpdateChecker.CheckAsync(client, "1.1.0", CancellationToken.None);

        Assert.True(result.IsNewerAvailable);
        Assert.Equal("1.2.0", result.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_InstalledMatchesAvailable_ReturnsNotNewer()
    {
        var client = new StubWingetClient("1.2.0");

        var result = await SelfUpdateChecker.CheckAsync(client, "1.2.0", CancellationToken.None);

        Assert.False(result.IsNewerAvailable);
        Assert.Equal("1.2.0", result.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_InstalledHasLeadingV_StillComparesCorrectly()
    {
        var client = new StubWingetClient("1.2.0");

        var result = await SelfUpdateChecker.CheckAsync(client, "v1.3.0", CancellationToken.None);

        Assert.False(result.IsNewerAvailable);
        Assert.Equal("1.2.0", result.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_DevBuild_ReturnsNotNewerButFillsAvailableVersion()
    {
        var client = new StubWingetClient("1.2.0");

        var result = await SelfUpdateChecker.CheckAsync(client, "0.0.0-dev", CancellationToken.None);

        Assert.False(result.IsNewerAvailable);
        Assert.Equal("1.2.0", result.AvailableVersion);
    }

    [Fact]
    public async Task CheckAsync_ClientThrows_ReturnsNotNewerAndNullAvailable()
    {
        var client = new ThrowingWingetClient();

        var result = await SelfUpdateChecker.CheckAsync(client, "1.0.0", CancellationToken.None);

        Assert.False(result.IsNewerAvailable);
        Assert.Null(result.AvailableVersion);
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

    // --- SelfUpdateCommand ---

    [Fact]
    public void DetachedUpgradeArgv_ContainsPackageIdAndExactFlag()
    {
        var argv = SelfUpdateCommand.DetachedUpgradeArgv();

        var payload = Assert.Single(argv, arg => arg.Contains("winget upgrade", StringComparison.Ordinal));
        Assert.Contains(SelfUpdateChecker.PackageId, payload);
        Assert.Contains("--exact", payload);
    }

    [Fact]
    public void Describe_ContainsPackageIdAndExactFlag()
    {
        var description = SelfUpdateCommand.Describe();

        Assert.Contains(SelfUpdateChecker.PackageId, description);
        Assert.Contains("--exact", description);
    }

    /// <summary>An <see cref="IWingetClient"/> whose <see cref="ShowAsync"/> always returns fixed details for <see cref="SelfUpdateChecker.PackageId"/>; every other member is unused by these tests.</summary>
    private sealed class StubWingetClient(string version) : IWingetClient
    {
        public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct) =>
            Task.FromResult<PackageDetails?>(new PackageDetails { Id = SelfUpdateChecker.PackageId, Version = version });

        public Task<string> GetVersionAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>An <see cref="IWingetClient"/> whose <see cref="ShowAsync"/> always throws; every other member is unused by these tests.</summary>
    private sealed class ThrowingWingetClient : IWingetClient
    {
        public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct) =>
            throw new InvalidOperationException("winget is unavailable.");

        public Task<string> GetVersionAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }
}
