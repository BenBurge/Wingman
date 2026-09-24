using System.Globalization;
using System.Text.Json.Serialization;

namespace Wingman.Core.Settings;

/// <summary>
/// User-configurable defaults, persisted as <c>settings.json</c> by <see cref="SettingsStore"/>.
/// </summary>
public sealed class WingmanSettings
{
    private const string DefaultAutoInstallTime = "03:00";

    /// <summary>
    /// Install scope winget should use by default: <c>""</c> lets winget decide, or <c>"user"</c>
    /// / <c>"machine"</c> to force one.
    /// </summary>
    public string DefaultScope { get; set; } = "";

    public bool AcceptAgreements { get; set; } = true;

    public bool IncludeUnknown { get; set; } = true;

    /// <summary>
    /// Which of a batch's operations run through the elevated helper. An unrecognized value in the
    /// file fails the whole load, and <see cref="SettingsStore.Load"/> falls back to defaults.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ElevationMode>))]
    public ElevationMode ElevationMode { get; set; } = ElevationMode.Auto;

    /// <summary>
    /// What the elevation prompt starts: <c>wingman.exe</c> directly, or Windows PowerShell running
    /// it. An unrecognized value in the file fails the whole load, as for <see cref="ElevationMode"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ElevationLauncher>))]
    public ElevationLauncher ElevationLauncher { get; set; } = ElevationLauncher.Direct;

    /// <summary>Keeps a batch running after one of its operations fails; when false, the rest are canceled.</summary>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>A theme name, or <c>Auto</c> to follow the system's light or dark mode.</summary>
    public string Theme { get; set; } = "Midnight";

    /// <summary>How often the background scheduler checks for updates. Clamped to 1..168 on load.</summary>
    public int CheckIntervalHours { get; set; } = 6;

    /// <summary>Runs an update check once at login, in addition to the regular interval.</summary>
    public bool CheckAtLogin { get; set; } = true;

    /// <summary>Installs available updates automatically at <see cref="AutoInstallTime"/> instead of only notifying.</summary>
    public bool AutoInstall { get; set; } = false;

    /// <summary>
    /// The daily time auto-install runs, as 24-hour <c>HH:mm</c>. An invalid value in the file
    /// falls back to <c>"03:00"</c> on load; see <see cref="AutoInstallTimeOfDay"/> for the parsed form.
    /// </summary>
    public string AutoInstallTime { get; set; } = DefaultAutoInstallTime;

    /// <summary><see cref="AutoInstallTime"/> parsed as a time of day, defaulting to 03:00 if unparsable.</summary>
    [JsonIgnore]
    public TimeOnly AutoInstallTimeOfDay =>
        TryParseAutoInstallTime(AutoInstallTime, out var timeOfDay) ? timeOfDay : new TimeOnly(3, 0);

    /// <summary>
    /// Lets the scheduled check download and run a newer Wingman installer from GitHub Releases.
    /// Only an installed copy updates itself; a portable one is left alone.
    /// </summary>
    public bool AutoUpdateWingman { get; set; } = true;

    public bool ToastOnUpdates { get; set; } = true;

    public bool ToastOnBatch { get; set; } = true;

    public bool ShowTrayIcon { get; set; } = true;

    public bool StartTrayAtLogin { get; set; } = true;

    /// <summary>Suppresses toast notifications without changing the other notification settings.</summary>
    public bool NotificationsPaused { get; set; } = false;

    /// <summary>
    /// Clamps and validates the fields above after deserializing, so a hand-edited or partially
    /// upgraded settings.json can never put the scheduler in an invalid state.
    /// </summary>
    public void Normalize()
    {
        CheckIntervalHours = Math.Clamp(CheckIntervalHours, 1, 168);

        if (!TryParseAutoInstallTime(AutoInstallTime, out _))
        {
            AutoInstallTime = DefaultAutoInstallTime;
        }
    }

    private static bool TryParseAutoInstallTime(string value, out TimeOnly timeOfDay) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out timeOfDay);
}
