using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Options;
using Wingman.Core.Updates;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class UpdatePolicyTests : IDisposable
{
    private static readonly PackageRow GitRow =
        new(Name: "Git", Id: "Git.Git", Version: "2.44.0", AvailableVersion: "2.55.0", Source: "winget");

    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private PackageOptionsStore CreateStore() => new(_directoryPath);

    private static Pin BlockingPin(string id) =>
        new(Id: id, Name: id, Version: "1.0", Source: "winget", PinType: PinType.Blocking, PinnedVersion: "");

    private static Pin GatingPin(string id) =>
        new(Id: id, Name: id, Version: "1.0", Source: "winget", PinType: PinType.Gating, PinnedVersion: "1.*");

    // --- UpdatePolicyResolver ---

    [Fact]
    public void Resolve_NoPinAndDefaultOptions_ReturnsUpdate()
    {
        var policy = UpdatePolicyResolver.Resolve(GitRow, [], new UpdatesOptions());

        Assert.Equal(UpdatePolicyKind.Update, policy);
    }

    [Fact]
    public void Resolve_BlockingPinForId_ReturnsHold()
    {
        var policy = UpdatePolicyResolver.Resolve(GitRow, [BlockingPin("Git.Git")], new UpdatesOptions());

        Assert.Equal(UpdatePolicyKind.Hold, policy);
    }

    [Fact]
    public void Resolve_BlockingPinIdIsCaseInsensitive_ReturnsHold()
    {
        var policy = UpdatePolicyResolver.Resolve(GitRow, [BlockingPin("git.git")], new UpdatesOptions());

        Assert.Equal(UpdatePolicyKind.Hold, policy);
    }

    [Fact]
    public void Resolve_GatingPin_DoesNotHold()
    {
        var policy = UpdatePolicyResolver.Resolve(GitRow, [GatingPin("Git.Git")], new UpdatesOptions());

        Assert.Equal(UpdatePolicyKind.Update, policy);
    }

    [Fact]
    public void Resolve_PinningPin_DoesNotHold()
    {
        var pin = new Pin("Git.Git", "Git", "1.0", "winget", PinType.Pinning, "");

        var policy = UpdatePolicyResolver.Resolve(GitRow, [pin], new UpdatesOptions());

        Assert.Equal(UpdatePolicyKind.Update, policy);
    }

    [Fact]
    public void Resolve_UpdatesIgnored_ReturnsExclude()
    {
        var policy = UpdatePolicyResolver.Resolve(GitRow, [], new UpdatesOptions { UpdatesIgnored = true });

        Assert.Equal(UpdatePolicyKind.Exclude, policy);
    }

    [Fact]
    public void Resolve_BlockingPinBeatsExclude()
    {
        var options = new UpdatesOptions { UpdatesIgnored = true };

        var policy = UpdatePolicyResolver.Resolve(GitRow, [BlockingPin("Git.Git")], options);

        Assert.Equal(UpdatePolicyKind.Hold, policy);
    }

    [Fact]
    public void Resolve_IgnoredVersionMatchesAvailableVersion_ReturnsSkipVersion()
    {
        var options = new UpdatesOptions { IgnoredVersion = "2.55.0" };

        var policy = UpdatePolicyResolver.Resolve(GitRow, [], options);

        Assert.Equal(UpdatePolicyKind.SkipVersion, policy);
    }

    [Fact]
    public void Resolve_IgnoredVersionComparisonIsCaseInsensitive()
    {
        var row = GitRow with { AvailableVersion = "2.55.0-BETA" };
        var options = new UpdatesOptions { IgnoredVersion = "2.55.0-beta" };

        var policy = UpdatePolicyResolver.Resolve(row, [], options);

        Assert.Equal(UpdatePolicyKind.SkipVersion, policy);
    }

    [Fact]
    public void Resolve_IgnoredVersionDoesNotMatchAvailableVersion_ReturnsUpdate()
    {
        var options = new UpdatesOptions { IgnoredVersion = "2.50.0" };

        var policy = UpdatePolicyResolver.Resolve(GitRow, [], options);

        Assert.Equal(UpdatePolicyKind.Update, policy);
    }

    // --- UpdatesFilter ---

    [Fact]
    public void Apply_DropsExcludedAndSkipped_KeepsHeldAndUpdate_CountsAllThree_PreservesOrder()
    {
        var held = new PackageRow("Held", "Held.Held", "1.0", "1.1", "winget");
        var excluded = new PackageRow("Excluded", "Excluded.Excluded", "1.0", "1.1", "winget");
        var skipped = new PackageRow("Skipped", "Skipped.Skipped", "1.0", "1.1", "winget");
        var normal = new PackageRow("Normal", "Normal.Normal", "1.0", "1.1", "winget");
        var upgradeRows = new List<PackageRow> { held, excluded, skipped, normal };
        var pins = new List<Pin> { BlockingPin("Held.Held") };

        var store = CreateStore();
        store.SetUpdatesOptions("Excluded.Excluded", new UpdatesOptions { UpdatesIgnored = true });
        store.SetUpdatesOptions("Skipped.Skipped", new UpdatesOptions { IgnoredVersion = "1.1" });

        var view = UpdatesFilter.Apply(upgradeRows, pins, store);

        Assert.Equal(2, view.Visible.Count);
        Assert.Equal("Held.Held", view.Visible[0].Row.Id);
        Assert.Equal(UpdatePolicyKind.Hold, view.Visible[0].Policy);
        Assert.Equal("Normal.Normal", view.Visible[1].Row.Id);
        Assert.Equal(UpdatePolicyKind.Update, view.Visible[1].Policy);
        Assert.Equal(1, view.HeldCount);
        Assert.Equal(1, view.ExcludedCount);
        Assert.Equal(1, view.SkippedCount);
    }

    // --- UpdatePolicyApplier ---

    [Fact]
    public async Task ApplyAsync_Hold_PinsAndClearsStore_ThenIsIdempotent()
    {
        var client = new FakeWingetClient(TimeSpan.Zero);
        var store = CreateStore();
        store.SetUpdatesOptions("Git.Git", new UpdatesOptions { UpdatesIgnored = true });
        var applier = new UpdatePolicyApplier(client, store);

        var firstResult = await applier.ApplyAsync(GitRow, UpdatePolicyKind.Hold, null, [], CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.NotEmpty(firstResult.Log);
        Assert.True(store.GetUpdatesOptions("Git.Git").IsDefault());

        var pins = await client.ListPinsAsync(CancellationToken.None);
        var pin = Assert.Single(pins);
        Assert.Equal(PinType.Blocking, pin.PinType);

        var secondResult = await applier.ApplyAsync(GitRow, UpdatePolicyKind.Hold, null, pins, CancellationToken.None);

        Assert.True(secondResult.Succeeded);
        Assert.Empty(secondResult.Log);
        var pinsAfterSecondCall = await client.ListPinsAsync(CancellationToken.None);
        Assert.Single(pinsAfterSecondCall);
    }

    [Fact]
    public async Task ApplyAsync_SkipVersion_SetsIgnoredVersionAndUnpins_ThenIsIdempotent()
    {
        var client = new FakeWingetClient(TimeSpan.Zero);
        await client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);
        var currentPins = await client.ListPinsAsync(CancellationToken.None);

        var store = CreateStore();
        var applier = new UpdatePolicyApplier(client, store);

        var firstResult = await applier.ApplyAsync(
            GitRow, UpdatePolicyKind.SkipVersion, null, currentPins, CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.NotEmpty(firstResult.Log);
        var options = store.GetUpdatesOptions("Git.Git");
        Assert.Equal("2.55.0", options.IgnoredVersion);
        Assert.False(options.UpdatesIgnored);
        Assert.Empty(await client.ListPinsAsync(CancellationToken.None));

        var secondResult = await applier.ApplyAsync(GitRow, UpdatePolicyKind.SkipVersion, null, [], CancellationToken.None);

        Assert.True(secondResult.Succeeded);
        Assert.Empty(secondResult.Log);
        Assert.Equal("2.55.0", store.GetUpdatesOptions("Git.Git").IgnoredVersion);
    }

    [Fact]
    public async Task ApplyAsync_Exclude_SetsUpdatesIgnoredAndUnpins_ThenIsIdempotent()
    {
        var client = new FakeWingetClient(TimeSpan.Zero);
        await client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);
        var currentPins = await client.ListPinsAsync(CancellationToken.None);

        var store = CreateStore();
        var applier = new UpdatePolicyApplier(client, store);

        var firstResult = await applier.ApplyAsync(
            GitRow, UpdatePolicyKind.Exclude, null, currentPins, CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.NotEmpty(firstResult.Log);
        var options = store.GetUpdatesOptions("Git.Git");
        Assert.True(options.UpdatesIgnored);
        Assert.Equal("", options.IgnoredVersion);
        Assert.Empty(await client.ListPinsAsync(CancellationToken.None));

        var secondResult = await applier.ApplyAsync(GitRow, UpdatePolicyKind.Exclude, null, [], CancellationToken.None);

        Assert.True(secondResult.Succeeded);
        Assert.Empty(secondResult.Log);
        Assert.True(store.GetUpdatesOptions("Git.Git").UpdatesIgnored);
    }

    [Fact]
    public async Task ApplyAsync_Update_ClearsStoreAndUnpins_ThenIsIdempotent()
    {
        var client = new FakeWingetClient(TimeSpan.Zero);
        await client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);
        var currentPins = await client.ListPinsAsync(CancellationToken.None);

        var store = CreateStore();
        store.SetUpdatesOptions("Git.Git", new UpdatesOptions { IgnoredVersion = "2.50.0" });
        var applier = new UpdatePolicyApplier(client, store);

        var firstResult = await applier.ApplyAsync(
            GitRow, UpdatePolicyKind.Update, null, currentPins, CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.NotEmpty(firstResult.Log);
        Assert.True(store.GetUpdatesOptions("Git.Git").IsDefault());
        Assert.Empty(await client.ListPinsAsync(CancellationToken.None));

        var secondResult = await applier.ApplyAsync(GitRow, UpdatePolicyKind.Update, null, [], CancellationToken.None);

        Assert.True(secondResult.Succeeded);
        Assert.Empty(secondResult.Log);
        Assert.True(store.GetUpdatesOptions("Git.Git").IsDefault());
    }

    [Fact]
    public async Task ApplyAsync_FailingPin_LeavesStoreUntouched()
    {
        var client = new FailingPinWingetClient();
        var store = CreateStore();
        store.SetUpdatesOptions("Git.Git", new UpdatesOptions { UpdatesIgnored = true });
        var applier = new UpdatePolicyApplier(client, store);

        var result = await applier.ApplyAsync(GitRow, UpdatePolicyKind.Hold, null, [], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(store.GetUpdatesOptions("Git.Git").UpdatesIgnored);
    }

    /// <summary>An <see cref="IWingetClient"/> whose <see cref="PinAsync"/> always fails; every other member is unused by these tests.</summary>
    private sealed class FailingPinWingetClient : IWingetClient
    {
        public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
            Task.FromResult(new OperationResult(1, false, TimeSpan.Zero, ["Pin failed."]));

        public Task<string> GetVersionAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }
}
