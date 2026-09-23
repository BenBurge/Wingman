using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiHarness;
using Wingman.Core.Bundles;
using Wingman.Core.Options;
using Wingman.Core.Winget;
using Wingman.Tui;

if (args.Length < 2 || !int.TryParse(args[0], out var width) || !int.TryParse(args[1], out var height))
{
    Console.Error.WriteLine("usage: TuiHarness <width> <height> [Midnight|Daylight]");
    return 2;
}

var theme = Theme.ByName(args.Length > 2 ? args[2] : null);

// A throwaway data directory, so the run never reads or writes the real settings and package
// options. Azd gets machine scope, which needs elevation, and a post-update command.
const string ElevatedId = "Microsoft.Azd";
const string ElevatedPostCommand = "azd config set defaults.location eastus2";
var dataDirectory = Path.Combine(Path.GetTempPath(), "wingman-harness-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(WingmanApp.DataDirectoryVariable, dataDirectory);
new PackageOptionsStore(dataDirectory).SetInstallOptions(ElevatedId, new InstallOptions
{
    InstallationScope = "machine",
    PostUpdateCommand = ElevatedPostCommand,
});
var settings = WingmanApp.CreateSettingsStore().Load();

using var app = Application.Create();
app.Init(DriverRegistry.Names.ANSI);

var screen = new Screen(app, width, height);
screen.Reset();
screen.HoldSize();

// A short step delay so an operation streams its ten lines in about half a second.
var client = new SlowClient(new FakeWingetClient(TimeSpan.FromMilliseconds(50)));
var shell = WingmanApp.CreateShell(app, theme, client, settings);
string Focused() => shell.Window.MostFocused?.GetType().Name ?? "none";

// Stand-ins so a run never overwrites the real clipboard or starts a browser.
app.Driver!.Clipboard = new FakeClipboard(false, false);
var openedUrls = new List<string>();
shell.OpenUrl = openedUrls.Add;

// The first table row on screen: the window border, tab strip, separator, filter row, blank row, and header come first.
const int FirstRowY = 6;
IReadOnlyList<string> before = [];

var failedChecks = 0;
void Check(string what, bool passed)
{
    screen.Log($"check {(passed ? "ok" : "FAILED")}: {what}");
    if (!passed)
    {
        failedChecks++;
    }
}

// The main loop redraws only what changed between steps, while Dump forces a full redraw, so
// comparing the rows read before a step's Dump with the rows after it catches a view that only
// looks right when everything is drawn again. On a mismatch this logs the incremental text; the
// frame above the check shows the full redraw.
void CheckSameText(string what, string incremental, string full)
{
    Check(what, incremental == full);
    if (incremental != full)
    {
        screen.Log("incremental text:\n" + incremental);
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

int Divider() => screen.Rows()[2].IndexOf('┬');
string LeftOf(string row) => row[..Divider()];
string RightPaneText(IReadOnlyList<string> rows) => string.Join("\n", rows.Skip(3).Take(rows.Count - 6).Select(row => row[(Divider() + 1)..]));
int KeyBarX(string item) => screen.Rows()[height - 2].IndexOf(item, StringComparison.Ordinal);
int TabStripX(string title) => screen.Rows()[1].IndexOf(" " + title + " ", StringComparison.Ordinal) + 1;
bool IsCursorRow(int y) => screen.AttributeAt(10, y) == theme.Selected.ToString();
bool IsMenuOpen() => ScreenHas("│ Copy id");

// Table rows whose marker column starts with the marked glyph; the Discover legend's ● is further right.
int MarkedRowCount() => screen.Rows().Skip(FirstRowY).Take(height - FirstRowY - 4).Count(row => row[1] == '●');
bool AnyMarkedRow(char ownMarker) => screen.Rows().Skip(FirstRowY).Take(height - FirstRowY - 4).Any(row => row[1] == '●' && row[2] == ownMarker);
bool IsHelpOpen() => ScreenHas("┌─ Keys ");

// The rows between the tab separator and the footer separator. The separators are left out: the
// window's line canvas sometimes redraws the footer separator's left end as │ rather than ├ between steps.
string PaneRows(IReadOnlyList<string> rows) => string.Join("\n", rows.Skip(3).Take(rows.Count - 7));

// The table rows below the filter row, whose count label animates during a load.
string LeftPaneRows(IReadOnlyList<string> rows) => string.Join("\n", rows.Skip(4).Take(rows.Count - 8).Select(LeftOf));
string HelpBoxArea(IReadOnlyList<string> rows)
{
    var top = rows.ToList().FindIndex(row => row.Contains("┌─ Keys ", StringComparison.Ordinal));
    if (top < 0)
    {
        return "";
    }

    var left = rows[top].IndexOf("┌─ Keys ", StringComparison.Ordinal);
    var right = rows[top].IndexOf('┐', left);
    return string.Join("\n", rows.Skip(top).Take(rows.Count - 4 - top).Select(row => row[left..(right + 1)]));
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
    new(50, "wheel down over the details pane scrolls the notes", () => { before = screen.Rows(); screen.Wheel(Divider() + 10, height - 8, down: true); }, Verify: () =>
        Check("notes moved", RightPaneText(screen.Rows()) != RightPaneText(before))),
    new(50, "wheel up over the details pane scrolls back", () => screen.Wheel(Divider() + 10, height - 8, down: false), Verify: () =>
        Check("notes back at the top", RightPaneText(screen.Rows()) == RightPaneText(before))),
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
    new(50, "wheel up over the log: older lines", () => screen.Wheel(Divider() + 10, height / 2, down: false), Verify: () =>
        Check("verdict scrolled out of view", !ScreenHas("Failed with exit code 1603"))),
    new(50, "wheel down over the log: newest lines", () => screen.Wheel(Divider() + 10, height / 2, down: true), Verify: () =>
        Check("verdict back in view", ScreenHas("Failed with exit code 1603"))),
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

    new(50, "Ctrl+Home, click the fourth row: the cursor moves to it", () => { screen.Press(Key.Home.WithCtrl); screen.Click(10, FirstRowY + 3); }, Verify: () =>
    {
        Check("fourth row is the cursor row", IsCursorRow(FirstRowY + 3));
        Check("first row is not", !IsCursorRow(FirstRowY));
    }),
    new(50, "double-click the fifth row: details focused", () => screen.DoubleClick(10, FirstRowY + 4), Verify: () =>
    {
        Check("fifth row is the cursor row", IsCursorRow(FirstRowY + 4));
        Check("details pane has focus", Focused() == nameof(DetailsPane));
    }),
    new(50, "wheel down over the table: rows scroll", () => { before = screen.Rows(); screen.Wheel(10, FirstRowY + 8, down: true); }, Verify: () =>
        Check("fourth row now first", LeftOf(screen.Rows()[FirstRowY]) == LeftOf(before[FirstRowY + 3]))),
    new(50, "wheel up over the table: rows scroll back", () => screen.Wheel(10, FirstRowY + 8, down: false), Verify: () =>
        Check("first row back", LeftOf(screen.Rows()[FirstRowY]) == LeftOf(before[FirstRowY]))),
    new(50, "click the Name header: sorted ascending", () => screen.Click(4, FirstRowY - 1), Verify: () =>
        Check("Name ▲", ScreenHas(" Name ▲"))),
    new(50, "click the Name header again: descending", () => screen.Click(4, FirstRowY - 1), Verify: () =>
        Check("Name ▼", ScreenHas(" Name ▼"))),
    new(50, "s, which the key bar leaves off: next column", () => screen.Press(Key.S), Verify: () =>
    {
        Check("Id ▲", ScreenHas(" Id ▲"));
        Check("s Sort not on the key bar", KeyBarX("s Sort") < 0);
    }),
    new(50, "click the filter box: it takes focus", () => screen.Click(12, 3), Verify: () =>
        Check("filter box has focus", Focused() == "TextField")),
    new(50, "filter GitHub.cli", () => { screen.Type("GitHub.cli"); screen.Press(Key.Enter); }),

    new(600, "right-click the row: context menu at the click", () => screen.RightClick(20, FirstRowY), WithColors: true, Verify: () =>
    {
        Check("menu title", ScreenHas("│ GitHub CLI "));
        Check("upgrade entry", ScreenHas("│ Upgrade to 2.101.0 "));
        Check("pin entry", ScreenHas("│ Pin "));
        Check("copy id entry", IsMenuOpen());
        Check("menu corner at the click", screen.Rows()[FirstRowY][20] == '┌');
    }),
    new(50, "Up x2 to Copy id, Enter", () => { screen.Press(Key.CursorUp, 2); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("menu closed", !IsMenuOpen());
        Check("copied message", ScreenHas("Copied GitHub.cli") || ScreenHas("Clipboard is not available"));
        Check("id on the clipboard", app.Clipboard!.TryGetClipboardData(out var copied) && copied == "GitHub.cli");
    }),
    new(50, "m: menu on the cursor row at column 24", () => screen.Press(Key.M), Verify: () =>
    {
        Check("menu open", IsMenuOpen());
        Check("menu corner at column 24 of the pane", screen.Rows()[FirstRowY][25] == '┌');
    }),
    new(50, "Down, Esc: menu closed", () => { screen.Press(Key.CursorDown); screen.Press(Key.Esc); }, Verify: () =>
    {
        Check("menu closed", !IsMenuOpen());
        Check("table keeps focus", Focused() == "KeyPassingTableView");
    }),
    new(50, "m, Up, Enter: open homepage", () => { screen.Press(Key.M); screen.Press(Key.CursorUp); screen.Press(Key.Enter); }),
    new(400, "homepage opened", () => { }, Verify: () =>
    {
        Check("homepage passed to the opener", openedUrls.Contains("https://example.invalid/GitHub.cli"));
        Check("opened message", ScreenHas("Opened https://example.invalid/GitHub.cli"));
    }),
    new(50, "m, then click outside: menu closed", () => { screen.Press(Key.M); screen.Click(Divider() + 20, height - 6); }, Verify: () =>
    {
        Check("menu closed", !IsMenuOpen());
        Check("table keeps focus", Focused() == "KeyPassingTableView");
    }),
    new(50, "m, then right-click outside: menu closed", () => { screen.Press(Key.M); screen.RightClick(Divider() + 20, height - 6); }, Verify: () =>
        Check("menu closed", !IsMenuOpen())),
    new(50, "right-click near the divider: the menu covers it",() => screen.RightClick(Divider() - 10, FirstRowY), Verify: () =>
    {
        var rows = screen.Rows();
        Check("menu open", IsMenuOpen());
        Check("divider hidden under the menu", Enumerable.Range(FirstRowY + 1, 9).All(y => rows[y][Divider()] != '│'));
    }),
    new(50, "the menu stays whole after incremental redraws", () => before = screen.Rows(), Verify: () =>
        CheckSameText("incremental panes match a full redraw", PaneRows(before), PaneRows(screen.Rows()))),
    new(50, "Esc", () => screen.Press(Key.Esc), Verify: () =>
        Check("menu closed", !IsMenuOpen())),
    new(50, "the panes redraw where the menu was", () => before = screen.Rows(), Verify: () =>
        CheckSameText("incremental panes match a full redraw", PaneRows(before), PaneRows(screen.Rows()))),

    new(50, "?: help on Installed", () => screen.Press(new Key('?')), WithColors: true, Verify: () =>
    {
        Check("help open", IsHelpOpen());
        Check("global group", ScreenHas("context menu"));
        Check("Installed group", ScreenHas("pin / unpin"));
    }),
    new(50, "x and 2 while help is open are ignored", () => { screen.Press(Key.X); screen.Press(new Key('2')); }, Verify: () =>
    {
        Check("help still open", IsHelpOpen());
        Check("no uninstall question", !ScreenHas("(y/n)"));
        Check("still on Installed", ScreenHas(" x Uninstall "));
    }),
    new(50, "Esc: help closed", () => screen.Press(Key.Esc), Verify: () =>
        Check("help closed", !IsHelpOpen())),
    new(50, "click ? Help on the key bar", () => screen.Click(KeyBarX("? Help"), height - 2), Verify: () =>
        Check("help open", IsHelpOpen())),
    new(50, "click outside the help box", () => screen.Click(3, height - 6), Verify: () =>
        Check("help closed", !IsHelpOpen())),

    new(50, "2, search Git.Git", () => { screen.Press(new Key('2')); screen.Press(new Key('/')); screen.Press(Key.Backspace, 6); screen.Type("Git.Git"); screen.Press(Key.Enter); }),
    new(600, "?: help on Discover", () => screen.Press(new Key('?')), Verify: () =>
    {
        Check("help open", IsHelpOpen());
        Check("Discover group", ScreenHas("on a row: details"));
    }),
    new(50, "q: help closed, app still running", () => screen.Press(Key.Q), Verify: () =>
    {
        Check("help closed", !IsHelpOpen());
        Check("still running", shell.Window.IsRunning);
    }),
    new(50, "right-click an installed result", () => screen.RightClick(20, FirstRowY), Verify: () =>
    {
        Check("install entry", ScreenHas("│ Install "));
        Check("uninstall entry for an installed package", ScreenHas("│ Uninstall "));
    }),
    new(50, "Esc", () => screen.Press(Key.Esc), Verify: () =>
        Check("menu closed", !IsMenuOpen())),

    new(50, "3, ?: help on Updates", () => { screen.Press(new Key('3')); screen.Press(new Key('?')); }, Verify: () =>
    {
        Check("help open", IsHelpOpen());
        Check("Updates group", ScreenHas("hold / release"));
    }),
    new(50, "Enter: help closed", () => screen.Press(Key.Enter), Verify: () =>
        Check("help closed", !IsHelpOpen())),
    new(50, "filter GitHub.cli, m", () => { screen.Press(new Key('/')); screen.Type("GitHub.cli"); screen.Press(Key.Enter); screen.Press(Key.M); }, Verify: () =>
    {
        Check("upgrade entry", ScreenHas("│ Upgrade to 2.101.0 "));
        Check("hold entry", ScreenHas("│ Hold "));
    }),
    new(50, "Enter on Upgrade: the question", () => screen.Press(Key.Enter), Verify: () =>
        Check("upgrade question", ScreenHas("Upgrade GitHub.cli to 2.101.0? (y/n)"))),
    new(50, "click n No on the key bar", () => screen.Click(KeyBarX("n No"), height - 2), Verify: () =>
    {
        Check("question gone", !ScreenHas("(y/n)"));
        Check("tab keys back", ScreenHas(" a Mark all "));
    }),
    new(50, "m, Enter on Upgrade again", () => { screen.Press(Key.M); screen.Press(Key.Enter); }),
    new(50, "click y Yes on the key bar", () => screen.Click(KeyBarX("y Yes"), height - 2)),
    new(200, "upgrade running", () => { }, Verify: () =>
        Check("running title", ScreenHas("▶ upgrade GitHub.cli"))),
    new(50, "?: help while the log shows", () => screen.Press(new Key('?')), Verify: () =>
    {
        Check("help open", IsHelpOpen());
        Check("log group", ScreenHas("scroll log"));
    }),
    new(150, "help stays whole while the log streams under it", () => before = screen.Rows(), Verify: () =>
        CheckSameText("incremental frame shows the same help box", HelpBoxArea(before), HelpBoxArea(screen.Rows()))),
    new(50, "Esc: help closed, the upgrade keeps running", () => screen.Press(Key.Esc), Verify: () =>
    {
        Check("help closed", !IsHelpOpen());
        Check("no cancel question", !ScreenHas("(y/n)"));
    }),
    new(50, "the table redraws where the help was", () => before = screen.Rows(), Verify: () =>
        CheckSameText("incremental table rows match a full redraw", LeftPaneRows(before), LeftPaneRows(screen.Rows()))),
    new(1000, "upgrade done, Enter: GitHub.cli leaves the filtered list", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("log gone", !ScreenHas("Done in "));
        Check("upgraded message", ScreenHas("Upgraded GitHub.cli in "));
    }),
    new(50, "click Installed on the tab strip", () => screen.Click(TabStripX("Installed"), 1), Verify: () =>
        Check("Installed keys", ScreenHas(" x Uninstall "))),

    new(50, "3, clear the filter, filter Azure", () => { screen.Press(new Key('3')); screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("Azure"); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("batch keys on the Updates bar", ScreenHas(" u Upgrade   ␣ Mark   a Mark all   c Clear   g Run   p Hold   r Refresh   ? Help   q Quit "));
        Check("three rows, nothing marked", ScreenHas("3 of 15 available") && MarkedRowCount() == 0);
    }),
    new(50, "Ctrl+Home, Space, Down, Space: two rows marked", () => { screen.Press(Key.Home.WithCtrl); screen.Press(Key.Space); screen.Press(Key.CursorDown); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("two marked rows", MarkedRowCount() == 2);
        Check("count shows the marks", ScreenHas("3 of 15 available · 2 marked"));
        Check("queue pane title", ScreenHas(" Queue  2 operations"));
        Check("Azd first", ScreenHas(" 1  " + ElevatedId));
        Check("Azd needs admin", ScreenHas("upgrade → 1.34.200") && ScreenHas("⚡") && ScreenHas(" admin"));
        Check("post command", ScreenHas("    post: azd config set"));
        Check("elevation summary", ScreenHas(" 1 of 2 need elevation."));
        Check("queue keys", ScreenHas(" g run queue   c clear"));
        Check("details pane gone", !ScreenHas(" Pinned     "));
    }),
    new(50, "a: marks the third", () => screen.Press(Key.A), WithColors: true, Verify: () =>
    {
        Check("three marked rows", MarkedRowCount() == 3);
        Check("count shows three marked", ScreenHas("3 of 15 available · 3 marked"));
        Check("queue pane title", ScreenHas(" Queue  3 operations"));
        Check("one of three elevated", ScreenHas(" 1 of 3 need elevation."));
        Check("UAC line", ScreenHas(" One UAC prompt will be shown."));
        Check("● in accent on a row off the cursor", screen.AttributeAt(1, FirstRowY) == theme.On(theme.Accent).ToString());
    }),
    new(50, "clear the filter: the marks stay", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); }, Verify: () =>
    {
        Check("count for the whole list", ScreenHas("15 available · 3 marked"));
        Check("three marked rows", MarkedRowCount() == 3);
    }),
    new(50, "filter Docker, p: hold it", () => { screen.Press(new Key('/')); screen.Type("Docker"); screen.Press(Key.Enter); screen.Press(Key.P); }),
    new(150, "clear the filter, a: marks all but held and explicit", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(Key.A); }, Verify: () =>
    {
        Check("count with held", ScreenHas("15 available · 13 marked · 1 held"));
        Check("held row not marked", !AnyMarkedRow('⊘'));
        Check("explicit-targeting row not marked", !AnyMarkedRow('!'));
        Check("queue pane title", ScreenHas(" Queue  13 operations"));
        Check("summary pinned at the bottom", ScreenHas(" 1 of 13 need elevation.") && ScreenHas(" g run queue   c clear"));
    }),
    new(50, "wheel down over the queue pane: entries scroll", () => { before = screen.Rows(); screen.Wheel(Divider() + 10, FirstRowY + 2, down: true); }, Verify: () =>
    {
        Check("entries moved", RightPaneText(screen.Rows()) != RightPaneText(before));
        Check("title stays", ScreenHas(" Queue  13 operations"));
        Check("keys stay", ScreenHas(" g run queue   c clear"));
    }),
    new(50, "c: queue cleared", () => screen.Press(Key.C), Verify: () =>
    {
        Check("cleared message", ScreenHas(Shell.QueueClearedText));
        Check("no marked rows", MarkedRowCount() == 0);
        Check("details pane back", ScreenHas(" Pinned     "));
        Check("count without marks", ScreenHas("15 available · 1 held"));
    }),
    new(50, "g: runner not here yet", () => screen.Press(Key.G), Verify: () =>
        Check("pending message", ScreenHas(Shell.BatchRunnerPendingText))),

    new(50, "1, filter Git.Git, Space: nothing to upgrade", () => { screen.Press(new Key('1')); screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("Git.Git"); screen.Press(Key.Enter); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("nothing-to-upgrade message", ScreenHas("Nothing to upgrade for Git.Git; use x to uninstall"));
        Check("batch keys on the Installed bar", ScreenHas(" ␣ Mark   c Clear   g Run   / Filter   ? Help   q Quit "));
        Check("nothing marked", MarkedRowCount() == 0);
    }),
    new(50, "filter ClaudeCode, Space: marked", () => { screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("ClaudeCode"); screen.Press(Key.Enter); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("marked row", MarkedRowCount() == 1);
        Check("count shows the mark", ScreenHas(" · 1 marked"));
        Check("queue pane title", ScreenHas(" Queue  1 operation"));
        Check("upgrade target", ScreenHas("upgrade → 2.1.268"));
        Check("no elevation", ScreenHas(" No elevation needed."));
    }),
    new(50, "2, search git", () => { screen.Press(new Key('2')); screen.Press(new Key('/')); screen.Press(Key.Backspace, 7); screen.Type("git"); screen.Press(Key.Enter); }),
    new(600, "Ctrl+Home, Space: Git.Git marked for install", () => { screen.Press(Key.Home.WithCtrl); screen.Press(Key.Space); }, WithColors: true, Verify: () =>
    {
        Check("installed and marked", AnyMarkedRow('✓') && LeftPaneHas("Git.Git"));
        Check("queue pane title", ScreenHas(" Queue  2 operations"));
        Check("install target", ScreenHas(" 2  Git.Git") && ScreenHas("install → latest"));
        Check("no elevation", ScreenHas(" No elevation needed."));
        Check("legend", ScreenHas("✓ installed   ● marked for batch"));
        Check("batch keys on the Discover bar", ScreenHas(" i Install   ␣ Mark   c Clear   g Run   ⏎ Search"));
    }),
    new(50, "m: Unmark in the menu", () => screen.Press(Key.M), Verify: () =>
        Check("unmark entry", ScreenHas("│ Unmark "))),
    new(50, "Down, Enter: Git.Git unmarked", () => { screen.Press(Key.CursorDown); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("no marked rows", MarkedRowCount() == 0);
        Check("queue pane title", ScreenHas(" Queue  1 operation"));
    }),
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

try
{
    Directory.Delete(dataDirectory, recursive: true);
}
catch (IOException)
{
}

screen.Log($"exited normally, {failedChecks} failed checks");
Console.WriteLine($"wrote {screen.OutputPath}, {failedChecks} failed checks");
return failedChecks == 0 ? 0 : 1;

internal sealed record Step(int DelayMs, string Label, Action Act, bool WithColors = false, Action? Verify = null);
