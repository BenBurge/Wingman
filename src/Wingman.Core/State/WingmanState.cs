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
}
