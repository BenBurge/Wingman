using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Reads package information from winget. Only the three read operations needed for phase 1's list
/// views are here; install/upgrade/uninstall/pin/show follow in phase 1 proper.
/// </summary>
public interface IWingetClient
{
    Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct);

    Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct);
}
