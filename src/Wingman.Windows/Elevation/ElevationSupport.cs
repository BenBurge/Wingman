using Wingman.Core.Elevation;

namespace Wingman.Windows.Elevation;

/// <summary>
/// The elevated-channel factory for <c>BatchRunner</c>, callable on any OS without a platform
/// guard of its own.
/// </summary>
public static class ElevationSupport
{
    /// <summary>
    /// <see cref="ElevatedHelperLauncher.StartAsync"/> on Windows; null elsewhere, where elevated
    /// operations run in-process.
    /// </summary>
    public static Func<CancellationToken, Task<IElevatedOperationChannel>>? Factory =>
        OperatingSystem.IsWindows() ? ElevatedHelperLauncher.StartAsync : null;
}
