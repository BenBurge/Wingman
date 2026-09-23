using Wingman.Core.Elevation;

namespace Wingman.Windows.Elevation;

/// <summary>
/// The Windows elevation pieces the TUI takes as plain values and delegates, callable on any OS
/// without a platform guard of its own.
/// </summary>
public static class ElevationSupport
{
    /// <summary>
    /// <see cref="ElevatedHelperLauncher.StartAsync"/> on Windows; null elsewhere, where elevated
    /// operations run in-process.
    /// </summary>
    public static Func<CancellationToken, Task<IElevatedOperationChannel>>? Factory =>
        OperatingSystem.IsWindows() ? ElevatedHelperLauncher.StartAsync : null;

    /// <summary><see cref="ElevationStatus.IsElevated"/> on Windows; null elsewhere.</summary>
    public static bool? IsElevated =>
        OperatingSystem.IsWindows() ? ElevationStatus.IsElevated() : null;

    /// <summary><see cref="SelfRelauncher.TryRestartAsAdministrator"/> on Windows; null elsewhere.</summary>
    public static Func<IReadOnlyList<string>, bool>? RestartAsAdministrator =>
        OperatingSystem.IsWindows() ? SelfRelauncher.TryRestartAsAdministrator : null;
}
