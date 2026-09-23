namespace Wingman.Core.Settings;

/// <summary>Reports whether the system is in light or dark mode, for the <c>Auto</c> theme.</summary>
public interface IThemeDetector
{
    /// <summary>True in light mode, false in dark mode, null when the system does not say.</summary>
    bool? IsLightMode();
}

/// <summary>The detector for systems Wingman cannot ask; <c>Auto</c> then picks the dark theme.</summary>
public sealed class DefaultThemeDetector : IThemeDetector
{
    public bool? IsLightMode() => null;
}
