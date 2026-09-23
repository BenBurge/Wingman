using Wingman.Core.Notifications;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;

namespace Wingman.Tui;

/// <summary>
/// The host's platform services for the TUI, each null where the platform has none, so the host
/// can pass one object on any OS.
/// </summary>
/// <param name="Setup">Registers the scheduled tasks and startup entry from the Settings tab.</param>
/// <param name="SelfUpdate">Starts the detached winget upgrade of Wingman itself; also turns on the startup check for a newer release.</param>
/// <param name="Toasts">Not used by the TUI yet; carried so the host wires every service in one place.</param>
/// <param name="Version">This build's version, compared against winget's for the self-update check.</param>
public sealed record ShellServices(ISetupExecutor? Setup, ISelfUpdateStarter? SelfUpdate, IToastSender? Toasts, string Version)
{
    /// <summary>No platform services, as off Windows or under <c>--fake</c>.</summary>
    public static ShellServices None { get; } = new(null, null, null, "");
}
