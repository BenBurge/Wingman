using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiHarness;
using Wingman.Core.Winget;
using Wingman.Tui;

if (args.Length < 2 || !int.TryParse(args[0], out var width) || !int.TryParse(args[1], out var height))
{
    Console.Error.WriteLine("usage: TuiHarness <width> <height> [Midnight|Daylight]");
    return 2;
}

var theme = Theme.ByName(args.Length > 2 ? args[2] : null);

using var app = Application.Create();
app.Init(DriverRegistry.Names.ANSI);

var screen = new Screen(app, width, height);
screen.Reset();

var shell = WingmanApp.CreateShell(app, theme, new SlowClient(new FakeWingetClient()));
string Focused() => shell.Window.MostFocused?.GetType().Name ?? "none";

// Each step waits DelayMs after the previous one, acts, then dumps the screen. The waits cover
// SlowClient's delays plus the details pane's 150 ms debounce.
(int DelayMs, string Label, Action Act, bool WithColors)[] steps =
[
    (100, "Installed loading", () => { }, false),
    (1500, "Installed loaded, details for the first row", () => { }, true),
    (50, "Down x3 quickly: row fields at once, details loading", () => screen.Press(Key.CursorDown, 3), false),
    (600, "Down x3 after the wait: details for the fourth row", () => { }, false),
    (50, "Ctrl+End: wide row, show fails", () => screen.Press(Key.End.WithCtrl), false),
    (600, "Ctrl+End after the wait: error in the pane", () => { }, true),
    (50, "Filter VisualStudioCode", () => { screen.Press(new Key('/')); screen.Type("VisualStudioCode"); screen.Press(Key.Enter); }, false),
    (600, "VS Code details from the captured show output", () => { }, false),
    (50, "Filter Git.Git", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("Git.Git"); screen.Press(Key.Enter); }, false),
    (600, "Git.Git details with release notes", () => { }, false),
    (50, "Tab to the pane, PageDown scrolls the notes", () => { screen.Press(Key.Tab); screen.Press(Key.PageDown); }, false),
    (50, "Tab back, clear the filter", () => { screen.Press(Key.Tab); screen.Press(new Key('/')); screen.Press(Key.Esc); }, false),
    (50, "Filter zzzz, Enter: empty list still takes tab keys", () => { screen.Press(new Key('/')); screen.Type("zzzz"); screen.Press(Key.Enter); }, false),
    (50, "clear the filter", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); }, false),
    (50, "2: Discover before any search", () => screen.Press(new Key('2')), false),
    (50, "Search git", () => { screen.Type("git"); screen.Press(Key.Enter); }, false),
    (800, "Discover after searching git", () => { }, true),
    (50, "Down x2", () => screen.Press(Key.CursorDown, 2), false),
    (600, "Down x2 after the wait", () => { }, false),
    (50, "Search zzzz", () => { screen.Press(new Key('/')); screen.Press(Key.Backspace, 3); screen.Type("zzzz"); screen.Press(Key.Enter); }, false),
    (600, "Discover after searching zzzz", () => { }, false),
    (50, "3: Updates loading", () => screen.Press(new Key('3')), false),
    (1000, "Updates loaded", () => { }, true),
    (50, "u on Updates", () => screen.Press(Key.U), false),
    (50, "2 then r: Discover reruns the last search", () => { screen.Press(new Key('2')); screen.Press(Key.R); }, false),
    (600, "Discover after the rerun", () => { }, false),
    (50, "q quits", () => screen.Press(Key.Q), false),
];

var next = 0;
void RunStep()
{
    var step = steps[next];
    step.Act();
    screen.Dump($"{step.Label} | focused={Focused()}", step.WithColors);
    next++;
    if (next < steps.Length && shell.Window.IsRunning)
    {
        app.AddTimeout(TimeSpan.FromMilliseconds(steps[next].DelayMs), () => { RunStep(); return false; });
    }
}

app.AddTimeout(TimeSpan.FromMilliseconds(steps[0].DelayMs), () => { RunStep(); return false; });
app.Run(shell.Window);
shell.Window.Dispose();

screen.Log("exited normally");
Console.WriteLine($"wrote {screen.OutputPath}");
return 0;
