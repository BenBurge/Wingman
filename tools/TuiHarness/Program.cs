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

// A short step delay so an operation streams its ten lines in about half a second.
var client = new SlowClient(new FakeWingetClient(TimeSpan.FromMilliseconds(50)));
var shell = WingmanApp.CreateShell(app, theme, client);
string Focused() => shell.Window.MostFocused?.GetType().Name ?? "none";

var failedChecks = 0;
void Check(string what, bool passed)
{
    screen.Log($"check {(passed ? "ok" : "FAILED")}: {what}");
    if (!passed)
    {
        failedChecks++;
    }
}

bool ScreenHas(string text) => screen.Rows().Any(row => row.Contains(text, StringComparison.Ordinal));

// The table's side of the split: the rows between the tab separator and the footer separator, left
// of the divider, found where it meets the tab separator as ┬.
bool LeftPaneHas(string text)
{
    var rows = screen.Rows();
    var divider = rows[2].IndexOf('┬');
    return rows.Skip(3).Take(rows.Count - 6).Any(row => row[..divider].Contains(text, StringComparison.Ordinal));
}

// Each step waits DelayMs after the previous one, acts, dumps the screen, then runs its checks.
// The waits cover SlowClient's delays, the fake's operation steps, and the details pane's 150 ms debounce.
Step[] steps =
[
    new(100, "Installed loading", () => { }),
    new(1500, "Installed loaded, details for the first row", () => { }, WithColors: true),
    new(50, "Down x3 quickly: row fields at once, details loading", () => screen.Press(Key.CursorDown, 3)),
    new(600, "Down x3 after the wait: details for the fourth row", () => { }),
    new(50, "Ctrl+End: wide row, show fails", () => screen.Press(Key.End.WithCtrl)),
    new(600, "Ctrl+End after the wait: error in the pane", () => { }, WithColors: true),
    new(50, "Filter VisualStudioCode", () => { screen.Press(new Key('/')); screen.Type("VisualStudioCode"); screen.Press(Key.Enter); }),
    new(600, "VS Code details from the captured show output", () => { }),
    new(50, "Filter Git.Git", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("Git.Git"); screen.Press(Key.Enter); }),
    new(600, "Git.Git details with release notes", () => { }),
    new(50, "Tab to the pane, PageDown scrolls the notes", () => { screen.Press(Key.Tab); screen.Press(Key.PageDown); }),
    new(50, "Tab back, clear the filter", () => { screen.Press(Key.Tab); screen.Press(new Key('/')); screen.Press(Key.Esc); }),
    new(50, "Filter zzzz, Enter: empty list still takes tab keys", () => { screen.Press(new Key('/')); screen.Type("zzzz"); screen.Press(Key.Enter); }),
    new(50, "clear the filter", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); }),
    new(50, "2: Discover before any search", () => screen.Press(new Key('2'))),
    new(50, "Search git", () => { screen.Type("git"); screen.Press(Key.Enter); }),
    new(800, "Discover after searching git", () => { }, WithColors: true),
    new(50, "Down x2", () => screen.Press(Key.CursorDown, 2)),
    new(600, "Down x2 after the wait", () => { }),
    new(50, "Search zzzz", () => { screen.Press(new Key('/')); screen.Press(Key.Backspace, 3); screen.Type("zzzz"); screen.Press(Key.Enter); }),
    new(600, "Discover after searching zzzz", () => { }),
    new(50, "3: Updates loading", () => screen.Press(new Key('3'))),
    new(1000, "Updates loaded", () => { }, WithColors: true),

    new(50, "u on AutoHotkey.AutoHotkey: confirmation prompt", () => screen.Press(Key.U), Verify: () =>
    {
        Check("upgrade question on the message line", ScreenHas("Upgrade AutoHotkey.AutoHotkey to 2.0.28? (y/n)"));
        Check("key bar shows y Yes and n No", ScreenHas(" y Yes   n No "));
    }),
    new(50, "x and Down while prompting are ignored", () => { screen.Press(Key.X); screen.Press(Key.CursorDown); }, Verify: () =>
        Check("question still up", ScreenHas("Upgrade AutoHotkey.AutoHotkey to 2.0.28? (y/n)"))),
    new(50, "y: the upgrade starts and the log replaces the details", () => screen.Press(Key.Y)),
    new(200, "Log streaming", () => { }, WithColors: true, Verify: () =>
    {
        Check("running title", ScreenHas("▶ upgrade AutoHotkey.AutoHotkey"));
        Check("command line", ScreenHas("$ winget upgrade --id AutoHotkey.A"));
        Check("running key bar", ScreenHas(" Esc Cancel   ↑↓ Scroll log   Tab Pane   q Quit "));
    }),
    new(50, "1 then x on Installed while running: refused", () => { screen.Press(new Key('1')); screen.Press(Key.X); }, Verify: () =>
        Check("refusal message", ScreenHas(Shell.AlreadyRunningText))),
    new(50, "3: back to Updates while the log streams", () => screen.Press(new Key('3'))),
    new(1800, "Upgrade done, Updates reloaded", () => { }, WithColors: true, Verify: () =>
    {
        Check("done title", ScreenHas("✓ upgrade AutoHotkey.AutoHotkey"));
        Check("done line", ScreenHas("Done in "));
        Check("success message", ScreenHas("Upgraded AutoHotkey.AutoHotkey in "));
        Check("finished key bar", ScreenHas(" ⏎ Back   ↑↓ Scroll log   q Quit "));
        Check("tab strip reads Updates 16", ScreenHas(" Updates 16 "));
        Check("AutoHotkey.AutoHotkey gone from the table", !LeftPaneHas("AutoHotkey.AutoHotkey"));
    }),
    new(50, "Enter: details pane back", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("details shown", ScreenHas(" Pinned     no"));
        Check("log gone", !ScreenHas("Done in "));
    }),
    new(50, "u, y on Microsoft.Azd", () => { screen.Press(Key.U); screen.Press(Key.Y); }),
    new(100, "q while running: quit prompt", () => screen.Press(Key.Q), Verify: () =>
        Check("quit question", ScreenHas("Quit and cancel upgrade Microsoft.Azd? (y/n)"))),
    new(50, "n: keep running", () => screen.Press(Key.N), Verify: () =>
    {
        Check("still running", ScreenHas("▶ upgrade Microsoft.Azd"));
        Check("running key bar back", ScreenHas(" Esc Cancel   ↑↓ Scroll log   Tab Pane   q Quit "));
    }),
    new(50, "Esc while running: cancel prompt", () => screen.Press(Key.Esc), Verify: () =>
        Check("cancel question", ScreenHas("Cancel upgrade Microsoft.Azd? (y/n)"))),
    new(50, "y: cancel", () => screen.Press(Key.Y)),
    new(400, "Canceled", () => { }, Verify: () =>
    {
        Check("canceled line", ScreenHas("Canceled after "));
        Check("canceled message", ScreenHas("Canceled upgrade Microsoft.Azd"));
    }),
    new(700, "Esc: details pane back", () => screen.Press(Key.Esc), Verify: () =>
        Check("Updates still 16", ScreenHas(" Updates 16 "))),

    new(50, "2 then r: Discover reruns the last search", () => { screen.Press(new Key('2')); screen.Press(Key.R); }),
    new(600, "Discover after the rerun", () => { }),
    new(50, "Search vendor", () => { screen.Press(new Key('/')); screen.Press(Key.Backspace, 4); screen.Type("vendor"); screen.Press(Key.Enter); }),
    new(600, "Discover after searching vendor", () => { }),
    new(50, "i, y on Vendor.WillFail", () => { screen.Press(Key.I); screen.Press(Key.Y); }),
    new(1200, "Install failed", () => { }, WithColors: true, Verify: () =>
    {
        Check("failed title", ScreenHas("✗ install " + SlowClient.FailingId));
        Check("failed line", ScreenHas("Failed with exit code 1603"));
        Check("failure message", ScreenHas($"install {SlowClient.FailingId} failed with exit code 1603"));
    }),
    new(50, "Enter: details pane back", () => screen.Press(Key.Enter)),

    new(50, "1, filter GitHub.cli", () => { screen.Press(new Key('1')); screen.Press(new Key('/')); screen.Type("GitHub.cli"); screen.Press(Key.Enter); }),
    new(600, "p: pin GitHub.cli", () => screen.Press(Key.P)),
    new(150, "GitHub.cli pinned", () => { }, WithColors: true, Verify: () =>
    {
        Check("pinned message", ScreenHas("Pinned GitHub.cli (blocking)"));
        Check("Pinned field", ScreenHas(" Pinned     yes (blocking)"));
        Check("pin marker", LeftPaneHas("⊘"));
        Check("key bar offers Unpin", ScreenHas(" p Unpin "));
    }),
    new(50, "3: Updates with GitHub.cli held", () => screen.Press(new Key('3')), WithColors: true, Verify: () =>
    {
        Check("held marker", LeftPaneHas("⊘ GitHub CLI"));
        Check("held legend first", ScreenHas("⊘ held (winget pin --blocking)  ! needs explicit"));
    }),
    new(50, "1, p: unpin GitHub.cli", () => { screen.Press(new Key('1')); screen.Press(Key.P); }),
    new(150, "GitHub.cli unpinned", () => { }, Verify: () =>
    {
        Check("unpinned message", ScreenHas("Unpinned GitHub.cli"));
        Check("Pinned field", ScreenHas(" Pinned     no"));
        Check("no pin marker", !LeftPaneHas("⊘"));
        Check("key bar offers Pin", ScreenHas(" p Pin "));
    }),
    new(50, "clear the filter", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); }),
    new(50, "q quits", () => screen.Press(Key.Q)),
];

var next = 0;
void RunStep()
{
    var step = steps[next];
    step.Act();
    screen.Dump($"{step.Label} | focused={Focused()}", step.WithColors);
    step.Verify?.Invoke();
    next++;
    if (next < steps.Length && shell.Window.IsRunning)
    {
        app.AddTimeout(TimeSpan.FromMilliseconds(steps[next].DelayMs), () => { RunStep(); return false; });
    }
}

app.AddTimeout(TimeSpan.FromMilliseconds(steps[0].DelayMs), () => { RunStep(); return false; });
app.Run(shell.Window);
shell.Window.Dispose();

screen.Log($"exited normally, {failedChecks} failed checks");
Console.WriteLine($"wrote {screen.OutputPath}, {failedChecks} failed checks");
return failedChecks == 0 ? 0 : 1;

internal sealed record Step(int DelayMs, string Label, Action Act, bool WithColors = false, Action? Verify = null);
