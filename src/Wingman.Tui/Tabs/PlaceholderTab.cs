using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Wingman.Tui.Tabs;

/// <summary>A tab whose real view has not been built yet; it shows one centered, dimmed line.</summary>
internal sealed class PlaceholderTab : ShellTab
{
    public PlaceholderTab(Theme theme, string title, string message)
        : base(title)
    {
        var label = new Label
        {
            X = Pos.Center(),
            Y = Pos.Center(),
            Text = message,
        };
        label.SetScheme(theme.DimScheme);
        Add(label);
    }
}
