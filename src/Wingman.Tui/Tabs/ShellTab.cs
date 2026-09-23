using Terminal.Gui.ViewBase;

namespace Wingman.Tui.Tabs;

/// <summary>
/// A page of the tab strip. It fills the shell's content area and renders its lines into the
/// window's line canvas, so a pane divider joins the separators above and below it as <c>┬</c>
/// and <c>┴</c>.
/// </summary>
internal abstract class ShellTab : View
{
    protected ShellTab(string title)
    {
        Title = title;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SuperViewRendersLineCanvas = true;
    }

    /// <summary>The tab's own key bar entries, shown before the global Help and Quit.</summary>
    public virtual IReadOnlyList<KeyHint> Hints => [];

    /// <summary>Called on the UI thread every time the tab becomes the active one.</summary>
    public virtual void OnShown()
    {
    }
}
