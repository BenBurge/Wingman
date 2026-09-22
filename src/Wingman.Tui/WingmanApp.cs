using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Wingman.Tui;

/// <summary>
/// Entry point for the terminal UI. Currently just the window shell with the three placeholder tabs
/// phase 1 will fill in.
/// </summary>
public static class WingmanApp
{
    public static void Run()
    {
        using var app = Application.Create();
        app.Init();

        var window = new Window { Title = "Wingman" };

        var tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        tabs.Add(new View { Title = "Installed" });
        tabs.Add(new View { Title = "Discover" });
        tabs.Add(new View { Title = "Updates" });

        var quit = new Shortcut(Key.Q, "Quit", () => window.RequestStop(), "Quit Wingman");
        var statusBar = new StatusBar([quit]);

        window.Add(tabs, statusBar);

        app.Run(window);
    }
}
