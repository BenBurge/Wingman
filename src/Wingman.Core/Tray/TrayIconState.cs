using System.Globalization;
using Wingman.Core.Settings;
using Wingman.Core.State;

namespace Wingman.Core.Tray;

/// <summary>The state badge drawn on the tray glyph, per docs/DESIGN.md.</summary>
public enum TrayBadge
{
    None,
    Updates,
    Working,
    Failed,
    Paused,
}

/// <summary>
/// Maps <see cref="WingmanState"/> and <see cref="WingmanSettings"/> to what the tray icon shows,
/// so the Windows tray process only has to load an icon file and set a tooltip.
/// </summary>
public static class TrayIconState
{
    private const string Prefix = "Wingman · ";

    /// <summary>
    /// Picks one badge by precedence: paused, then working, then failed, then updates available.
    /// Paused wins because the user chose it and it explains why no toasts arrive.
    /// </summary>
    public static TrayBadge From(WingmanState state, WingmanSettings settings)
    {
        if (settings.NotificationsPaused)
        {
            return TrayBadge.Paused;
        }

        if (state.Running)
        {
            return TrayBadge.Working;
        }

        var lastRunFailed = state.LastBatchFailed || state.LastError != "";
        if (lastRunFailed)
        {
            return TrayBadge.Failed;
        }

        if (state.UpdatesAvailable > 0)
        {
            return TrayBadge.Updates;
        }

        return TrayBadge.None;
    }

    /// <summary>
    /// The file under <c>assets/tray/</c> for <paramref name="badge"/>. Only the plain glyph has a
    /// dark variant for light taskbars; the badged glyphs are drawn once, in white.
    /// </summary>
    public static string IconFileName(TrayBadge badge, bool lightTaskbar) => badge switch
    {
        TrayBadge.Updates => "wingman-tray-updates.ico",
        TrayBadge.Working => "wingman-tray-working.ico",
        TrayBadge.Failed => "wingman-tray-failed.ico",
        TrayBadge.Paused => "wingman-tray-paused.ico",
        _ => lightTaskbar ? "wingman-tray-dark.ico" : "wingman-tray.ico",
    };

    /// <summary>The tray tooltip, with the last check time shown in the local time zone.</summary>
    public static string Tooltip(WingmanState state, WingmanSettings settings) =>
        Tooltip(state, settings, TimeZoneInfo.Local);

    internal static string Tooltip(WingmanState state, WingmanSettings settings, TimeZoneInfo timeZone)
    {
        switch (From(state, settings))
        {
            case TrayBadge.Paused:
                return Prefix + "notifications paused";
            case TrayBadge.Working:
                return Prefix + "working…";
            case TrayBadge.Failed:
                return Prefix + "last run failed";
            case TrayBadge.Updates:
                var noun = state.UpdatesAvailable == 1 ? "update" : "updates";
                return Prefix + $"{state.UpdatesAvailable} {noun} available";
        }

        if (state.LastCheck is not { } lastCheck)
        {
            return Prefix + "never checked";
        }

        var localTime = TimeZoneInfo.ConvertTime(lastCheck, timeZone);
        var clock = localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        return Prefix + $"up to date (checked {clock})";
    }
}
