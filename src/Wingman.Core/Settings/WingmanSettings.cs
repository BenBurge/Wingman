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

    public string DefaultSource { get; set; } = "winget";

    public bool AcceptAgreements { get; set; } = true;

    public bool IncludeUnknown { get; set; } = true;

    /// <summary>
    /// Runs a batch's operations that need elevation through one elevated helper, behind a single
    /// UAC prompt; when false, they run in-process and each installer prompts for itself.
    /// </summary>
    public bool AutoElevate { get; set; } = true;

    /// <summary>Keeps a batch running after one of its operations fails; when false, the rest are canceled.</summary>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>A theme name, or <c>Auto</c> to follow the system's light or dark mode.</summary>
    public string Theme { get; set; } = "Midnight";
}
