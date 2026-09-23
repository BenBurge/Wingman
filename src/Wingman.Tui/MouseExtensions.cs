using Terminal.Gui.Input;

namespace Wingman.Tui;

internal static class MouseExtensions
{
    private const MouseFlags LeftClicks =
        MouseFlags.LeftButtonClicked | MouseFlags.LeftButtonDoubleClicked | MouseFlags.LeftButtonTripleClicked;

    /// <summary>
    /// True for every left click. A second click soon after the first arrives as a double-click
    /// rather than another click, so a control that acts per click has to accept all three.
    /// </summary>
    public static bool IsLeftClick(this Mouse mouse) => (mouse.Flags & LeftClicks) != 0;
}
