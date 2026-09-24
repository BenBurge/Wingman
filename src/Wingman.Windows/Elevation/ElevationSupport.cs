using System.Runtime.Versioning;
using Wingman.Core.Elevation;
using Wingman.Core.Settings;

namespace Wingman.Windows.Elevation;

/// <summary>
/// The Windows elevation pieces the TUI takes as plain values and delegates, callable on any OS
/// without a platform guard of its own.
/// </summary>
public static class ElevationSupport
{
    // The folder override the TUI and the CLI honor; the launcher must read the settings file they write.
    private const string DataDirectoryVariable = "WINGMAN_DATA_DIR";

    /// <summary>
    /// <see cref="ElevatedHelperLauncher.StartAsync(ElevationLauncher, CancellationToken)"/> with
    /// <see cref="Launcher"/> on Windows; null elsewhere, where elevated operations run in-process.
    /// </summary>
    public static Func<CancellationToken, Task<IElevatedOperationChannel>>? Factory =>
        OperatingSystem.IsWindows() ? StartHelperAsync : null;

    /// <summary><see cref="ElevationStatus.IsElevated"/> on Windows; null elsewhere.</summary>
    public static bool? IsElevated =>
        OperatingSystem.IsWindows() ? ElevationStatus.IsElevated() : null;

    /// <summary>
    /// <see cref="SelfRelauncher.TryRestartAsAdministrator"/> with <see cref="Launcher"/> on
    /// Windows; null elsewhere.
    /// </summary>
    public static Func<IReadOnlyList<string>, bool>? RestartAsAdministrator =>
        OperatingSystem.IsWindows() ? RestartAsAdministratorWith : null;

    /// <summary>
    /// <see cref="WingmanSettings.ElevationLauncher"/> as saved in <c>settings.json</c>, read at
    /// every prompt rather than once at startup, so a change saved on the Settings tab applies to
    /// the next batch or restart and the CLI follows the same setting, without either host passing
    /// it along. <see cref="ElevationLauncher.Direct"/> when the file cannot be read.
    /// </summary>
    public static ElevationLauncher Launcher
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            var store = string.IsNullOrWhiteSpace(directory) ? SettingsStore.CreateDefault() : new SettingsStore(directory);
            try
            {
                return store.Load().ElevationLauncher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ElevationLauncher.Direct;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static Task<IElevatedOperationChannel> StartHelperAsync(CancellationToken ct) =>
        ElevatedHelperLauncher.StartAsync(Launcher, ct);

    [SupportedOSPlatform("windows")]
    private static bool RestartAsAdministratorWith(IReadOnlyList<string> args) =>
        SelfRelauncher.TryRestartAsAdministrator(Launcher, args);
}
