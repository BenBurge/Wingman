using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

/// <summary>
/// A <see cref="FakeWingetClient"/> with two answers a test can replace: what <c>winget show</c>
/// returns for one id, and an error for <c>winget upgrade</c> to throw.
/// </summary>
internal sealed class ScriptedWingetClient(FakeWingetClient inner) : IWingetClient
{
    /// <summary>Details <see cref="ShowAsync"/> returns for <see cref="ShownId"/>; other ids go to the fake.</summary>
    public PackageDetails? Shown { get; set; }

    public string ShownId { get; set; } = "";

    public Exception? ListUpgradesError { get; set; }

    public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct)
    {
        if (string.Equals(id, ShownId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Shown);
        }

        return inner.ShowAsync(id, ct);
    }

    public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct)
    {
        if (ListUpgradesError is { } error)
        {
            throw error;
        }

        return inner.ListUpgradesAsync(ct);
    }

    public Task<string> GetVersionAsync(CancellationToken ct) => inner.GetVersionAsync(ct);

    public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct) => inner.SearchAsync(query, ct);

    public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct) => inner.ListInstalledAsync(ct);

    public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) => inner.ListVersionsAsync(id, ct);

    public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => inner.ListPinsAsync(ct);

    public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        inner.InstallAsync(request, output, ct);

    public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        inner.UpgradeAsync(request, output, ct);

    public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        inner.UninstallAsync(request, output, ct);

    public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
        inner.PinAsync(id, blocking, version, ct);

    public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => inner.UnpinAsync(id, ct);
}
