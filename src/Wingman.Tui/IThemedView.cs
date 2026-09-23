namespace Wingman.Tui;

/// <summary>
/// A view that draws with colors from a <see cref="Theme"/> it holds. <see cref="Shell.ApplyTheme"/>
/// calls <see cref="ApplyTheme"/> on every such view in the window, hidden ones included, and then
/// redraws them, so a theme switch recolors the app without a restart.
/// </summary>
internal interface IThemedView
{
    /// <summary>Draws with <paramref name="theme"/> from now on, including the schemes it gives its own plain subviews.</summary>
    void ApplyTheme(Theme theme);
}
