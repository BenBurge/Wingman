namespace Wingman.Core.State;

/// <summary>
/// The scheduler's last-known status, persisted as <c>state.json</c> by <see cref="StateStore"/>.
/// Unlike <see cref="Wingman.Core.Settings.WingmanSettings"/>, this is written by
/// <c>wingman check</c> and the tray rather than by the user, and read by the tray to render
/// itself without running a check of its own.
/// </summary>
public sealed class WingmanState
{
    /// <summary>When the last update check completed, or <c>null</c> if none has ever run.</summary>
    public DateTimeOffset? LastCheck { get; set; }

    public int UpdatesAvailable { get; set; }

    public List<string> UpdateIds { get; set; } = [];

    /// <summary>A short summary of the last auto-install batch, e.g. <c>"3 of 3 updated"</c>.</summary>
    public string LastBatchResult { get; set; } = "";

    public bool LastBatchFailed { get; set; }

    public string LastError { get; set; } = "";

    /// <summary>Set while a check or batch is in progress, so the tray can show an in-progress state.</summary>
    public bool Running { get; set; }

    public DateTimeOffset? LastBatch { get; set; }

    /// <summary>
    /// When the latest GitHub release was last read, so the TUI and the scheduled check share one
    /// request every few hours; <c>null</c> if it never has been.
    /// </summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>The latest release's version without its <c>v</c>, or empty when none was found.</summary>
    public string LatestVersion { get; set; } = "";

    public string LatestTag { get; set; } = "";

    public string LatestSetupUrl { get; set; } = "";

    public string LatestSha256Url { get; set; } = "";

    /// <summary>
    /// The version <c>check --notify</c> started the installer for; the first check that runs as
    /// that version announces the update with a toast and clears it.
    /// </summary>
    public string PendingUpdateVersion { get; set; } = "";
}
