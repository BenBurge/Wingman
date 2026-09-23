using Wingman.Core.Notifications;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;
using Wingman.Core.Winget;
using Wingman.Windows.Notifications;

namespace Wingman.Windows;

/// <summary>
/// The Windows implementations of setup, toasts, and self-update, as nullable values the host can
/// hand to the CLI and TUI on any OS without a platform guard of its own.
/// </summary>
public static class HostServices
{
    /// <summary>A <see cref="Setup.SetupExecutor"/> on Windows; null elsewhere, where there is nothing to register.</summary>
    public static ISetupExecutor? SetupExecutor(IProcessRunner runner) =>
        OperatingSystem.IsWindows() ? new Setup.SetupExecutor(runner) : null;

    /// <summary>A <see cref="ToastNotifier"/> on Windows; null elsewhere, where no toasts are shown.</summary>
    public static IToastSender? ToastSender(IProcessRunner runner) =>
        OperatingSystem.IsWindows() ? new ToastNotifier(runner) : null;

    /// <summary>A <see cref="SelfUpdate.SelfUpdateStarter"/> on Windows; null elsewhere, where Wingman is not installed through winget.</summary>
    public static ISelfUpdateStarter? SelfUpdateStarter() =>
        OperatingSystem.IsWindows() ? new SelfUpdate.SelfUpdateStarter() : null;
}
