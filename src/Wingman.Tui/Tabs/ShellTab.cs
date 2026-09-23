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

    /// <summary>False to leave <c>? Help</c> off the key bar after <see cref="Hints"/>; <c>q Quit</c> is always there.</summary>
    public virtual bool ShowsHelpHint => true;

    /// <summary>The tab's keys as the help overlay lists them, after the global group; none by default.</summary>
    public virtual IReadOnlyList<HelpGroup> HelpGroups => [];

    /// <summary>Called on the UI thread every time the tab becomes the active one.</summary>
    public virtual void OnShown()
    {
    }

    /// <summary>Opens the context menu for the cursor row, for <c>m</c>; does nothing on a tab without rows.</summary>
    public virtual void ShowContextMenu()
    {
    }
}
