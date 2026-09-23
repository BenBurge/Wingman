using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Everything Wingman asks of winget: reading the version, searching, listing installed packages,
/// upgrades, and pins, showing a package's details and versions, and installing, upgrading,
/// uninstalling, pinning, and unpinning one package at a time.
/// </summary>
public interface IWingetClient
{
    /// <summary>The trimmed output of <c>winget --version</c>, such as <c>v1.29.380</c>.</summary>
    Task<string> GetVersionAsync(CancellationToken ct);

    Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct);

    /// <summary>Installed packages with an upgrade available, including those whose installed version is unknown.</summary>
    Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct);

    /// <summary>The package's manifest details, or null when winget finds no package with that id.</summary>
    Task<PackageDetails?> ShowAsync(string id, CancellationToken ct);

    /// <summary>Every version winget can install for the package, newest first.</summary>
    Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct);

    Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct);

    /// <summary>Installs the package, reporting each line winget prints to <paramref name="output"/>.</summary>
    Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct);

    /// <summary>Upgrades the package, reporting each line winget prints to <paramref name="output"/>.</summary>
    Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct);

    /// <summary>
    /// Uninstalls the package, reporting each line winget prints to <paramref name="output"/>. The
    /// request's architecture and skip-hash-check settings do not apply to uninstall and are ignored.
    /// </summary>
    Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct);

    /// <summary>
    /// Pins the package. <paramref name="blocking"/> makes it a blocking pin; <paramref name="version"/>
    /// names the version or range, such as <c>1.2.*</c>, that a gating pin holds to.
    /// </summary>
    Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct);

    Task<OperationResult> UnpinAsync(string id, CancellationToken ct);
}
