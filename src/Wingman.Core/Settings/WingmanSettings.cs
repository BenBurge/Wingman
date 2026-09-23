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

    public string Theme { get; set; } = "Midnight";
}
