using Wingman.Core.Notifications;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;

namespace Wingman.Tui;

/// <summary>
/// The host's platform services for the TUI, each null where the platform has none, so the host
/// can pass one object on any OS.
/// </summary>
/// <param name="Setup">Registers the scheduled tasks and startup entry from the Settings tab.</param>
/// <param name="SelfUpdate">Runs a downloaded Wingman installer; with <see cref="Releases"/>, also turns on the startup check for a newer release.</param>
/// <param name="Toasts">Not used by the TUI yet; carried so the host wires every service in one place.</param>
/// <param name="Version">This build's version, compared against the latest GitHub release.</param>
public sealed record ShellServices(ISetupExecutor? Setup, ISelfUpdateStarter? SelfUpdate, IToastSender? Toasts, string Version)
{
    /// <summary>No platform services, as off Windows or under <c>--fake</c>.</summary>
    public static ShellServices None { get; } = new(null, null, null, "");

    /// <summary>Reads the latest Wingman release from GitHub.</summary>
    public GitHubReleaseSource? Releases { get; init; }

    /// <summary>Downloads and verifies a release's installer for <c>Update Wingman</c>.</summary>
    public UpdateDownloader? Downloader { get; init; }

    /// <summary>The runtime whose installer is downloaded: <c>win-x64</c> or <c>win-arm64</c>.</summary>
    public string Rid { get; init; } = "win-x64";

    /// <summary>Where <c>Update Wingman</c> downloads installers to.</summary>
    public string UpdateDirectory { get; init; } = UpdateDownloader.DefaultDirectory;
}
