using System.Text.Json.Serialization;

namespace Wingman.Core.Settings;

/// <summary>
/// User-configurable defaults, persisted as <c>settings.json</c> by <see cref="SettingsStore"/>.
/// </summary>
public sealed class WingmanSettings
{
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

    /// <summary>Keeps a batch running after one of its operations fails; when false, the rest are canceled.</summary>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>A theme name, or <c>Auto</c> to follow the system's light or dark mode.</summary>
    public string Theme { get; set; } = "Midnight";
}
