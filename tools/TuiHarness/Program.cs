using System.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiHarness;
using Wingman.Core.Bundles;
using Wingman.Core.History;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui;

if (args.Length < 2 || !int.TryParse(args[0], out var width) || !int.TryParse(args[1], out var height))
{
    Console.Error.WriteLine("usage: TuiHarness <width> <height> [Midnight|Daylight|Nord|Dracula] [--elevated]");
    return 2;
}

// The normal run ends by launching the harness again with --elevated, which plays a short scenario
// with the shell told it already runs as administrator and appends its frames to the same out.txt.
const string ElevatedFlag = "--elevated";
var isElevatedRun = args.Contains(ElevatedFlag);
var themeName = args.Length > 2 && args[2] != ElevatedFlag ? args[2] : null;

var themeDetector = new DefaultThemeDetector();
var theme = Theme.ByName(themeName, themeDetector);

// A throwaway data directory, so the run never reads or writes the real settings and package
// options. Azd gets machine scope, which needs elevation, and a post-update command.
const string ElevatedId = "Microsoft.Azd";
const string ElevatedPostCommand = "azd config set defaults.location eastus2";

// The Updates row the update policy steps exclude and restore.
const string PolicyId = "JanDeDobbeleer.OhMyPosh";
var dataDirectory = Path.Combine(Path.GetTempPath(), "wingman-harness-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(WingmanApp.DataDirectoryVariable, dataDirectory);
new PackageOptionsStore(dataDirectory).SetInstallOptions(ElevatedId, new InstallOptions
{
    InstallationScope = "machine",
    PostUpdateCommand = ElevatedPostCommand,
});
var settingsStore = WingmanApp.CreateSettingsStore();
var settings = settingsStore.Load();

// Disposed before the elevated run starts, so the two never share the console.
var app = Application.Create();
app.Init(DriverRegistry.Names.ANSI);

var screen = new Screen(app, width, height);
if (!isElevatedRun)
{
    screen.Reset();
}

screen.HoldSize();

// Stands in for restarting as administrator: records the arguments and reports the prompt declined.
var restartRequests = new List<IReadOnlyList<string>>();
bool RecordRestart(IReadOnlyList<string> restartArgs)
{
    restartRequests.Add(restartArgs);
    return false;
}

// A short step delay so an operation streams its nine lines in under half a second.
var client = new SlowClient(new FakeWingetClient(TimeSpan.FromMilliseconds(40)));
var shell = WingmanApp.CreateShell(
    app, theme, client, settingsStore, settings, themeDetector, elevation: null, new FakeCommandRunner(),
    processIsElevated: isElevatedRun, restartAsAdministrator: RecordRestart);
var historyDirectory = Path.Combine(dataDirectory, "history");
string Focused() => shell.Window.MostFocused?.GetType().Name ?? "none";

// The export screen writes into the data directory, since WINGMAN_DATA_DIR is set; the import steps
// read the captured UniGetUI bundle from a copy there, which the run deletes with the rest.
var exportPath = Path.Combine(dataDirectory, $"{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}.ubundle");
var bundleCopy = Path.Combine(dataDirectory, "bundle-unigetui.ubundle");
var harnessDirectory = Path.GetDirectoryName(screen.OutputPath)!;
File.Copy(Path.Combine(harnessDirectory, "..", "..", "tests", "Wingman.Core.Tests", "Fixtures", "bundle-unigetui.ubundle"), bundleCopy);
int InstalledCount() => shell.InstalledRows.Count;
int CompatibleCount() => shell.InstalledRows.Count(BundleExporter.IsCompatible);
int CompatibleIndex(int nth) => shell.InstalledRows.Select((row, index) => (row, index)).Where(item => BundleExporter.IsCompatible(item.row)).ElementAt(nth).index;

// The install options editor's rows: a label padded to its column, then the box's opening bracket.
string EditorLabel(string label) => " " + label.PadRight(22);

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
string RowWith(string text) => screen.Rows().FirstOrDefault(row => row.Contains(text, StringComparison.Ordinal)) ?? "";

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

// The right pane's text as one line, so a check can find a sentence the pane wraps at 96 columns.
string RightPaneFlowed() => string.Join(" ", screen.Rows().Skip(3).Take(height - 6).Select(row => row[(Divider() + 1)..].Trim(' ', '│')).Where(text => text.Length > 0));
int KeyBarX(string item) => screen.Rows()[height - 2].IndexOf(item, StringComparison.Ordinal);
int TabStripX(string title) => screen.Rows()[1].IndexOf(" " + title + " ", StringComparison.Ordinal) + 1;
bool IsCursorRow(int y) => y >= 0 && screen.AttributeAt(10, y) == theme.Selected.ToString();
bool IsMenuOpen() => ScreenHas("│ Copy id");

// The History steps: the entry count before forgetting one, and the entry forgotten.
var historyCount = 0;
HistoryEntry? forgotten = null;

// Clicks one cell into the first on-screen occurrence of text, such as "( ) Daylight".
void ClickText(string text)
{
    var rows = screen.Rows();
    for (var y = 0; y < rows.Count; y++)
    {
        var x = rows[y].IndexOf(text, StringComparison.Ordinal);
        if (x >= 0)
        {
            screen.Click(x + 1, y);
            return;
        }
    }
}

// Table rows whose marker column starts with the marked glyph; the Discover legend's ● is further right.
int MarkedRowCount() => screen.Rows().Skip(FirstRowY).Take(height - FirstRowY - 4).Count(row => row[1] == '●');
bool AnyMarkedRow(char ownMarker) => screen.Rows().Skip(FirstRowY).Take(height - FirstRowY - 4).Any(row => row[1] == '●' && row[2] == ownMarker);
bool IsHelpOpen() => ScreenHas("┌─ Keys ");

// The batch screen draws each operation as " ✓ Id …" from the window border, so the glyph is at column 2 and the Id starts at 4.
int BatchRowY(string id) => screen.Rows().ToList().FindIndex(row => row.Length > 4 && row[4..].StartsWith(id + " ", StringComparison.Ordinal));
string BatchRow(string id) => BatchRowY(id) is var y && y >= 0 ? screen.Rows()[y] : "";
char BatchGlyph(string id) => BatchRow(id) is { Length: > 2 } row ? row[2] : ' ';
bool HasBarOrSpinner(string row) => row.Contains('%') || "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏".Any(row.Contains);

// The rows between the tab separator and the footer separator. The separators are left out: the
// window's line canvas sometimes redraws the footer separator's left end as │ rather than ├ between steps.
string PaneRows(IReadOnlyList<string> rows) => string.Join("\n", rows.Skip(3).Take(rows.Count - 7));

string HelpBoxArea(IReadOnlyList<string> rows)
{
    var top = rows.ToList().FindIndex(row => row.Contains("┌─ Keys ", StringComparison.Ordinal));
    if (top < 0)
    {
        return "";
    }

    var left = rows[top].IndexOf("┌─ Keys ", StringComparison.Ordinal);
    var right = rows[top].IndexOf('┐', left);

    // Only the box: the rows under it keep changing while a batch streams and finishes.
    var bottom = rows.ToList().FindIndex(top, row => row.Length > left && row[left] == '└');
    var boxRows = bottom < 0 ? rows.Count - 4 - top : bottom - top + 1;
    return string.Join("\n", rows.Skip(top).Take(boxRows).Select(row => row[left..(right + 1)]));
}

// The shell told it already runs as administrator: the title bar badge, the queue pane and batch
// screen naming it, Retry elevated refused, and the restart action dim.
Step[] elevatedSteps =
[
    new(1500, "elevated: Installed loaded", () => { }, WithColors: true, Verify: () =>
    {
        Check("admin badge left of the winget version", screen.Rows()[0].Contains("─ admin ─ winget ", StringComparison.Ordinal));
        var badgeX = screen.Rows()[0].IndexOf(" admin ", StringComparison.Ordinal) + 1;
        Check("badge in the success color", screen.AttributeAt(badgeX, 0) == theme.On(theme.Ok, Terminal.Gui.Drawing.TextStyle.Bold).ToString());
    }),
    new(50, "elevated: 3", () => screen.Press(new Key('3'))),
    new(1000, "elevated: filter Microsoft.Azd, Space", () => { screen.Press(new Key('/')); screen.Type(ElevatedId); screen.Press(Key.Enter); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("queued", shell.Queue.Count == 1 && shell.Queue.Contains(ElevatedId));
        Check("still marked as needing admin", ScreenHas("⚡") && ScreenHas(" 1 of 1 need elevation."));
        Check("the helper line names the elevated process", RightPaneFlowed().Contains("elevated helper: running as administrator", StringComparison.Ordinal));
        Check("no UAC sentence", !ScreenHas("One UAC prompt"));
    }),
    new(50, "elevated: g", () => screen.Press(Key.G), Verify: () =>
        Check("batch title says so", ScreenHas("elevated helper: running as administrator "))),
    new(1000, "elevated: the batch finished in-process", () => { }, Verify: () =>
    {
        Check("finished", ScreenHas(" Batch finished  1 of 1") && !ScreenHas("failed"));
        Check("still running as administrator", ScreenHas("elevated helper: running as administrator "));
    }),
    new(50, "elevated: Enter, 2, search vendor", () => { screen.Press(Key.Enter); screen.Press(new Key('2')); screen.Type("vendor"); screen.Press(Key.Enter); }),
    new(600, "elevated: i, y on Vendor.WillFail", () => { screen.Press(Key.I); screen.Press(Key.Y); }),
    new(1500, "elevated: the install failed", () => { }, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  1 of 1 · 1 failed"));
        Check("failure key bar", ScreenHas(" R Retry   I Interactive   S Skip hash check   A Retry elevated   l Log   ⏎ Back   q Quit "));
    }),
    new(50, "elevated: A is refused", () => screen.Press(Key.A), Verify: () =>
    {
        Check("status", ScreenHas(Shell.AlreadyElevatedText));
        Check("no new batch", ScreenHas(" Batch finished  1 of 1 · 1 failed"));
    }),
    new(50, "elevated: Enter, 5: Settings", () => { screen.Press(Key.Enter); screen.Press(new Key('5')); }, WithColors: true, Verify: () =>
    {
        Check("restart dim with the reason", ScreenHas("   ⏎ Restart as administrator   already administrator"));
        var restartY = screen.Rows().ToList().FindIndex(row => row.Contains("⏎ Restart as administrator", StringComparison.Ordinal));
        Check("drawn dim", restartY >= 0 && screen.AttributeAt(4, restartY) == theme.On(theme.Dim).ToString());
    }),
    new(50, "elevated: q quits", () => screen.Press(Key.Q)),
];

// Each step waits DelayMs after the previous one, acts, dumps the screen, then runs its checks.
// The waits cover SlowClient's delays, the fake's operation steps, and the details pane's 150 ms debounce.
Step[] mainSteps =
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

    new(50, "b: the bundle chooser on the message line", () => screen.Press(Key.B), Verify: () =>
    {
        Check("chooser question", ScreenHas("Bundle: e export, i import, Esc cancel"));
        Check("chooser keys", ScreenHas(" e Export   i Import   Esc Cancel "));
    }),
    new(50, "e: the export screen", () => screen.Press(Key.E), WithColors: true, Verify: () =>
    {
        Check("title", ScreenHas(" Export bundle  UniGetUI-compatible .ubundle · export_version 3"));
        Check("include options", ScreenHas(" [x] Include per-package install options   [x] Include holds and ignored versions"));
        Check("marked only", ScreenHas(" [ ] Only marked rows"));
        Check("header", ScreenHas("     " + "Name".PadRight(21) + "Id".PadRight(27) + "Version".PadRight(10) + "Notes"));
        Check("a row winget cannot reinstall", ScreenHas(" ⊘   ActiveBatch V14") && ScreenHas("skipped · not from winget"));
        Check("a checked winget row", ScreenHas("[x]  Adobe Acrobat Reader"));
        Check("more below", ScreenHas(" more"));
        Check("count", ScreenHas($" {CompatibleCount()} of {InstalledCount()} selected · {InstalledCount() - CompatibleCount()} skipped: winget cannot reinstall them"));
        Check("file row", ScreenHas(" File  [ "));
        Check("export key bar", ScreenHas(" ⏎ Export   ␣ Toggle   a All   n None   m Marked only   Esc Cancel   ? Help   q Quit "));
        Check("the list has focus", Focused() == nameof(CheckTable));
    }),
    new(50, "n, then Space on two winget rows", () =>
    {
        screen.Press(Key.N);
        screen.Press(Key.CursorDown, CompatibleIndex(0));
        screen.Press(Key.Space);
        screen.Press(Key.CursorDown, CompatibleIndex(1) - CompatibleIndex(0));
        screen.Press(Key.Space);
    }, Verify: () =>
        Check("two selected", ScreenHas($" 2 of {InstalledCount()} selected"))),
    new(50, "Enter: exported", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("exported status ending in the file name", ScreenHas("Exported 2 packages to ") && ScreenHas(Path.GetFileName(exportPath) + " "));
        Check("table back", ScreenHas("Filter:"));
        var exported = BundleSerializer.Read(File.ReadAllText(exportPath));
        Check("the file parses with two packages", exported.Packages.Count == 2 && exported.Packages.All(package => package.ManagerName == "WinGet"));
        Check("the rest listed as incompatible", exported.IncompatiblePackages.Count == InstalledCount() - CompatibleCount());
        Check("no byte order mark", File.ReadAllBytes(exportPath)[0] == (byte)'{');
    }),
    new(50, "b, i: the import screen asks for the file", () => { screen.Press(Key.B); screen.Press(Key.I); }, Verify: () =>
    {
        Check("title", ScreenHas(" Import bundle  choose a .ubundle file"));
        Check("the export offered first", shell.LastBundlePath == exportPath && ScreenHas(" File  [ "));
        Check("file key bar", ScreenHas(" ⏎ Read   Esc Cancel   ? Help   q Quit "));
    }),
    new(50, "type the fixture's path, Enter: the plan", () =>
    {
        screen.Press(Key.End);
        screen.Press(Key.Backspace, exportPath.Length);
        screen.Type(bundleCopy);
        screen.Press(Key.Enter);
    }, WithColors: true, Verify: () =>
    {
        Check("title", ScreenHas(" Import bundle  bundle-unigetui.ubundle · 6 packages"));
        Check("header", ScreenHas("     " + "Name".PadRight(20) + "Id".PadRight(25) + "Bundle".PadRight(9) + "Installed".PadRight(10) + "Plan"));
        Check("install with options", RowWith(" 7zip.7zip ").Contains("—") && RowWith(" 7zip.7zip ").Contains("install  ⚙ options"));
        Check("upgrade", RowWith(" AutoHotkey.AutoHotkey ").Contains(" 2.0.26 ") && RowWith(" AutoHotkey.AutoHotkey ").Contains("upgrade"));
        Check("keep", RowWith(" Obsidian.Obsidian ").Contains("keep") && RowWith(" Git.Git ").Contains("keep"));
        Check("⊘ Scoop package", RowWith(" ripgrep ").Contains(" ⊘ ") && ScreenHas("⊘ Scoop package"));
        Check("⊘ incompatible", ScreenHas("⊘ incompatible"));
        Check("summary", ScreenHas(" Plan  1 install · 1 upgrade · 2 already installed · 2 skipped"));
        Check("apply options", ScreenHas("       [x] Apply the bundle's install options to package-options.json"));
        Check("holds, dim", ScreenHas("       [x] Apply holds as winget pins"));
        Check("elevation", ScreenHas("       1 of 2 operations need elevation. One UAC prompt will be shown."));
        Check("plan key bar", ScreenHas(" ⏎ Run plan   ␣ Toggle   o View options   a All   n None   Esc Cancel   ? Help   q Quit "));
    }),
    new(50, "Down x2, o: 7-Zip's options from the bundle", () => { screen.Press(Key.CursorDown, 2); screen.Press(Key.O); }, Verify: () =>
        Check("options on the message line", ScreenHas("7zip.7zip: scope=machine, skipHash"))),
    new(50, "Up, Space: AutoHotkey left out, so only 7-Zip runs", () => { screen.Press(Key.CursorUp); screen.Press(Key.Space); }, Verify: () =>
        Check("elevation for one", ScreenHas("       1 of 1 operations need elevation."))),
    new(50, "Enter: the run question", () => screen.Press(Key.Enter), Verify: () =>
        Check("question", ScreenHas("Run 1 operation from bundle-unigetui.ubundle? (y/n)"))),
    new(50, "y: the plan runs as a batch on Installed", () => screen.Press(Key.Y), Verify: () =>
    {
        Check("running title", ScreenHas(" Running batch  1 of 1"));
        Check("install row", BatchRow("7zip.7zip").Contains("install  → latest"));
        Check("the bundle's scope needs elevation", ScreenHas("elevated helper: not available "));
        Check("options stored", shell.Options.GetInstallOptions("7zip.7zip") is { InstallationScope: "machine", SkipHashCheck: true });
    }),
    new(1000, "the import batch finished", () => { }, Verify: () =>
        Check("finished title", ScreenHas(" Batch finished  1 of 1") && !ScreenHas("failed"))),
    new(50, "Enter: Installed is back", () => screen.Press(Key.Enter), Verify: () =>
        Check("table back", ScreenHas("Filter:"))),

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
    new(50, "n, then Space: AutoHotkey.AutoHotkey queued", () => { screen.Press(Key.N); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("question gone", !ScreenHas("(y/n)"));
        Check("AutoHotkey queued", shell.Queue.Count == 1 && shell.Queue.Contains("AutoHotkey.AutoHotkey"));
    }),
    new(50, "2, search vendor", () => { screen.Press(new Key('2')); screen.Press(new Key('/')); screen.Press(Key.Backspace, 4); screen.Type("vendor"); screen.Press(Key.Enter); }),
    new(600, "Space: Vendor.WillFail queued for install", () => screen.Press(Key.Space), Verify: () =>
        Check("both queued", shell.Queue.Count == 2 && shell.Queue.Contains(SlowClient.FailingId))),
    new(50, "3, g: the batch screen takes the content area", () => { screen.Press(new Key('3')); screen.Press(Key.G); }, Verify: () =>
    {
        Check("running title", ScreenHas(" Running batch  1 of 2"));
        Check("no elevation needed", ScreenHas("elevated helper: not needed "));
        Check("running key bar", ScreenHas(" Esc Cancel remaining   ↑↓ Scroll log   q Quit "));
        Check("table hidden", !ScreenHas("Filter:"));
        Check("upgrade action", BatchRow("AutoHotkey.AutoHotkey").Contains("upgrade  2.0.26 → 2.0.28"));
        Check("install action", BatchRow(SlowClient.FailingId).Contains("install  → latest"));
        Check("install waiting", BatchGlyph(SlowClient.FailingId) == '·' && BatchRow(SlowClient.FailingId).Contains("waiting"));
    }),
    new(600, "mid-batch: one done, one running", () => { }, WithColors: true, Verify: () =>
    {
        Check("title counts the running one", ScreenHas(" Running batch  2 of 2"));
        Check("AutoHotkey done", BatchGlyph("AutoHotkey.AutoHotkey") == '✓' && BatchRow("AutoHotkey.AutoHotkey").Contains("done"));
        Check("install running", BatchGlyph(SlowClient.FailingId) == '▶');
        Check("bar or spinner", HasBarOrSpinner(BatchRow(SlowClient.FailingId)));
        Check("log follows the running one", ScreenHas(" ─ Log · " + SlowClient.FailingId + " ─"));
    }),
    new(900, "batch finished: the one failed row is selected and diagnosed", () => { }, WithColors: true, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  2 of 2 · 1 failed"));
        Check("failed row", BatchGlyph(SlowClient.FailingId) == '✗' && BatchRow(SlowClient.FailingId).Contains("failed  exit 1603"));
        Check("failed row selected", IsCursorRow(BatchRowY(SlowClient.FailingId)));
        Check("failure rule", ScreenHas(" ─ " + SlowClient.FailingId + " failed ─"));
        Check("failure key bar", ScreenHas(" R Retry   I Interactive   S Skip hash check   A Retry elevated   l Log   ⏎ Back   q Quit "));
    }),
    new(50, "Up: the done row and its log", () => screen.Press(Key.CursorUp), Verify: () =>
    {
        Check("first row selected", IsCursorRow(BatchRowY("AutoHotkey.AutoHotkey")));
        Check("finished key bar", ScreenHas(" ⏎ Back   ↑↓ Select   l Full log   q Quit "));
        Check("its log", ScreenHas(" ─ Log · AutoHotkey.AutoHotkey ─"));
        Check("its log saved", ScreenHas(" Log is saved to history/") && ScreenHas("-upgrade-AutoHotkey.AutoHotkey.log"));
    }),
    new(50, "Down to the failed row: the failure panel", () => screen.Press(Key.CursorDown), WithColors: true, Verify: () =>
    {
        Check("failed row selected", IsCursorRow(BatchRowY(SlowClient.FailingId)));
        Check("winget said", ScreenHas(" winget said     Fatal error during installation."));
        Check("code and name", ScreenHas(" Code            1603  ERROR_INSTALL_FAILURE"));
        Check("usually means", ScreenHas(" Usually means   Usually the app is running, or a previous install is broken."));
        Check("suggestion", ScreenHas(" Suggestion      Close it and retry interactive."));
        Check("last log lines", ScreenHas(" Last log lines") && ScreenHas(" Installer failed with exit code: 1603"));
        Check("no log rule", !ScreenHas(" ─ Log · "));
        Check("its history file", ScreenHas("-install-" + SlowClient.FailingId + ".log"));
        Check("failure key bar", ScreenHas(" R Retry   I Interactive "));
    }),
    new(50, "l: the whole log fills the screen", () => screen.Press(Key.L), Verify: () =>
    {
        Check("title and rows hidden", !ScreenHas("Batch finished") && BatchRow(SlowClient.FailingId).Length == 0);
        Check("rule on the first row", screen.Rows()[3].Contains(" ─ Log · " + SlowClient.FailingId));
        Check("installer failure line", ScreenHas("Installer failed with exit code: 1603"));
    }),
    new(50, "wheel up over the log: older lines", () => screen.Wheel(width / 2, height - 8, down: false), Verify: () =>
        Check("failure line scrolled out of view", !ScreenHas("Installer failed with exit code: 1603"))),
    new(50, "wheel down over the log: newest lines", () => screen.Wheel(width / 2, height - 8, down: true), Verify: () =>
        Check("failure line back in view", ScreenHas("Installer failed with exit code: 1603"))),
    new(50, "l again: rows and the failure panel back", () => screen.Press(Key.L), Verify: () =>
    {
        Check("title back", ScreenHas(" Batch finished  2 of 2 · 1 failed"));
        Check("failure panel back", ScreenHas(" Code            1603"));
    }),
    new(50, "S: retry skipping the hash check", () => screen.Press(Key.S), Verify: () =>
    {
        Check("a new batch of one", ScreenHas(" Running batch  1 of 1") && BatchRow(SlowClient.FailingId).Contains("install  → latest"));
        Check("the old batch's done row gone", BatchRow("AutoHotkey.AutoHotkey").Length == 0);
    }),
    new(1200, "the retry failed too", () => { }, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  1 of 1 · 1 failed"));
        Check("failure panel", ScreenHas(" Code            1603  ERROR_INSTALL_FAILURE"));
        var retry = new HistoryStore(historyDirectory).List().First(entry => entry.PackageId == SlowClient.FailingId);
        Check("the retry passed --ignore-security-hash", retry.Arguments.Contains("--ignore-security-hash"));
    }),
    new(50, "A: retry elevated, with no helper to start", () => screen.Press(Key.A), Verify: () =>
    {
        Check("a new batch of one", ScreenHas(" Running batch  1 of 1") && BatchRow(SlowClient.FailingId).Contains("install  → latest"));
        Check("the helper is needed but not available", ScreenHas("elevated helper: not available "));
    }),
    new(1200, "the elevated retry ran in-process and failed", () => { }, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  1 of 1 · 1 failed"));
        Check("failure panel", ScreenHas(" Code            1603  ERROR_INSTALL_FAILURE"));
    }),
    new(50, "Shift+E: exclude the failed package", () => screen.Press(new Key('E')), Verify: () =>
    {
        Check("excluded status", ScreenHas("Excluded " + SlowClient.FailingId + " from Wingman updates"));
        Check("stored as UpdatesIgnored", shell.Options.GetUpdatesOptions(SlowClient.FailingId).UpdatesIgnored);
    }),
    new(50, "Enter: the list is back", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("table back", ScreenHas("Filter:"));
        Check("queue empty", shell.Queue.Count == 0 && !ScreenHas(" Queue  "));
    }),
    new(600, "Updates reloaded without AutoHotkey", () => { }, WithColors: true, Verify: () =>
    {
        Check("tab strip reads Updates 16", ScreenHas(" Updates 16 "));
        Check("AutoHotkey.AutoHotkey gone from the table", !LeftPaneHas("AutoHotkey.AutoHotkey"));
        Check("details pane back", ScreenHas(" Policy     update"));
        Check("seeded options for Azd", ScreenHas(" Options    custom (o to edit)"));
        var entries = new HistoryStore(historyDirectory).List();
        Check("five operation entries and four batch entries, the import's included", Directory.GetFiles(historyDirectory, "*.json").Length == 9
            && entries.Count(entry => entry.Operation == "batch") == 4
            && entries.Count(entry => entry.Operation != "batch") == 5);
    }),

    new(50, "Space on Microsoft.Azd, Down, Space, g", () => { screen.Press(Key.Space); screen.Press(Key.CursorDown); screen.Press(Key.Space); screen.Press(Key.G); }, Verify: () =>
    {
        Check("two operations", ScreenHas(" Running batch  1 of 2") && BatchRow(ElevatedId).Length > 0);
        Check("elevated but no helper", ScreenHas("elevated helper: not available "));
    }),
    new(100, "Esc while running: cancel prompt", () => screen.Press(Key.Esc), Verify: () =>
        Check("cancel question", ScreenHas("Cancel remaining operations? (y/n)"))),
    new(50, "y: cancel", () => screen.Press(Key.Y)),
    new(400, "batch canceled", () => { }, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  0 of 2 · 2 canceled"));
        Check("both rows canceled", screen.Rows().Count(row => row.Length > 4 && row[2] == '○' && row.Contains("canceled")) == 2);
        Check("the log of Azd says so", ScreenHas(" Canceled"));
    }),
    new(50, "Esc: the list is back", () => screen.Press(Key.Esc), Verify: () =>
        Check("Updates still 16", ScreenHas(" Updates 16 "))),

    new(50, "2 then r: Discover reruns the last search", () => { screen.Press(new Key('2')); screen.Press(Key.R); }),
    new(600, "Discover after the rerun", () => { }),
    new(50, "i, y on Vendor.WillFail: a batch of one", () => { screen.Press(Key.I); screen.Press(Key.Y); }, Verify: () =>
    {
        Check("running title", ScreenHas(" Running batch  1 of 1"));
        Check("install row", BatchRow(SlowClient.FailingId).Contains("install  → latest"));
    }),
    new(50, "q while running: quit prompt", () => screen.Press(Key.Q), Verify: () =>
        Check("quit question", ScreenHas("Quit and cancel the batch? (y/n)"))),
    new(50, "n: keep running", () => screen.Press(Key.N), Verify: () =>
    {
        Check("still running", ScreenHas(" Running batch  1 of 1"));
        Check("running key bar back", ScreenHas(" Esc Cancel remaining   ↑↓ Scroll log   q Quit "));
    }),
    new(50, "1 then x on Installed while running: refused", () => { screen.Press(new Key('1')); screen.Press(Key.X); }, Verify: () =>
        Check("refusal message", ScreenHas(Shell.BatchRunningText))),
    new(50, "g on Installed while running: refused", () => screen.Press(Key.G), Verify: () =>
        Check("refusal message", ScreenHas(Shell.BatchRunningText))),
    new(50, "2: back to the running batch", () => screen.Press(new Key('2')), Verify: () =>
        Check("batch screen back", ScreenHas(" ─ Log · " + SlowClient.FailingId + " ─") || ScreenHas(" ─ " + SlowClient.FailingId + " failed ─"))),
    new(1200, "install failed", () => { }, WithColors: true, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  1 of 1 · 1 failed"));
        Check("failed row", BatchRow(SlowClient.FailingId).Contains("failed  exit 1603"));
        Check("failure line", ScreenHas("Installer failed with exit code: 1603"));
    }),
    new(50, "Enter: search results back", () => screen.Press(Key.Enter), Verify: () =>
        Check("table back", ScreenHas("Search:"))),

    new(50, "1, filter GitHub.cli", () => { screen.Press(new Key('1')); screen.Press(new Key('/')); screen.Type("GitHub.cli"); screen.Press(Key.Enter); }),
    new(600, "p: the update policy dialog for GitHub.cli", () => screen.Press(Key.P), Verify: () =>
    {
        Check("dialog title", ScreenHas(" Update policy  GitHub.cli · installed 2.98.0 · available 2.101.0"));
        Check("update chosen", ScreenHas(" (•) Update with Wingman"));
        Check("hold level", ScreenHas(" ( ) Hold at 2.98.0"));
        Check("dialog key bar", ScreenHas(" ⏎ Save   Esc Cancel   ↑↓ Choose   Tab Note   ? Help   q Quit "));
        Check("table hidden", !ScreenHas("Filter:"));
    }),
    new(50, "Down, Enter: hold GitHub.cli", () => { screen.Press(Key.CursorDown); screen.Press(Key.Enter); }),
    new(150, "GitHub.cli held", () => { }, WithColors: true, Verify: () =>
    {
        Check("held message", ScreenHas("Held GitHub.cli"));
        Check("Policy field", ScreenHas(" Policy     hold (blocking)"));
        Check("pin marker", LeftPaneHas("⊘"));
        Check("key bar offers Policy", ScreenHas(" p Policy "));
    }),
    new(50, "3: Updates with GitHub.cli held", () => screen.Press(new Key('3')), WithColors: true, Verify: () =>
    {
        Check("held marker", LeftPaneHas("⊘ GitHub CLI"));
        Check("held legend first", ScreenHas("⊘ held (winget pin --blocking)  ! needs explicit"));
    }),
    new(50, "1, p: the dialog chooses Hold", () => { screen.Press(new Key('1')); screen.Press(Key.P); }, Verify: () =>
        Check("hold chosen", ScreenHas(" (•) Hold at 2.98.0"))),
    new(50, "Up, Enter: GitHub.cli updates with Wingman again", () => { screen.Press(Key.CursorUp); screen.Press(Key.Enter); }),
    new(150, "GitHub.cli released", () => { }, Verify: () =>
    {
        Check("released message", ScreenHas("GitHub.cli updates with Wingman"));
        Check("Policy field", ScreenHas(" Policy     update"));
        Check("no pin marker", !LeftPaneHas("⊘"));
        Check("key bar offers Policy", ScreenHas(" p Policy "));
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
        Check("policy entry", ScreenHas("│ Update policy… "));
        Check("options entry", ScreenHas("│ Install options… "));
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
        Check("Installed group", ScreenHas("install options") && ScreenHas("uninstall"));
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
        Check("Updates group", ScreenHas("list excluded packages"));
    }),
    new(50, "Enter: help closed", () => screen.Press(Key.Enter), Verify: () =>
        Check("help closed", !IsHelpOpen())),
    new(50, "filter GitHub.cli, m", () => { screen.Press(new Key('/')); screen.Type("GitHub.cli"); screen.Press(Key.Enter); screen.Press(Key.M); }, Verify: () =>
    {
        Check("upgrade entry", ScreenHas("│ Upgrade to 2.101.0 "));
        Check("policy entry", ScreenHas("│ Update policy… "));
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
        Check("running row", BatchGlyph("GitHub.cli") == '▶')),
    new(50, "?: help while the batch runs", () => screen.Press(new Key('?')), Verify: () =>
    {
        Check("help open", IsHelpOpen());
        Check("batch group", ScreenHas("back when done"));
    }),
    new(150, "help stays whole while the log streams under it", () => before = screen.Rows(), Verify: () =>
        CheckSameText("incremental frame shows the same help box", HelpBoxArea(before), HelpBoxArea(screen.Rows()))),
    new(50, "Esc: help closed, the upgrade keeps running", () => screen.Press(Key.Esc), Verify: () =>
    {
        Check("help closed", !IsHelpOpen());
        Check("no cancel question", !ScreenHas("(y/n)"));
    }),
    new(1000, "upgrade done: the screen redraws where the help was", () => before = screen.Rows(), Verify: () =>
    {
        Check("finished", ScreenHas(" Batch finished  1 of 1"));
        CheckSameText("incremental panes match a full redraw", PaneRows(before), PaneRows(screen.Rows()));
    }),
    new(50, "Enter: the list is back and GitHub.cli has left it", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("screen gone", !ScreenHas("Batch finished"));
        Check("GitHub CLI gone from the filtered list", !LeftPaneHas("GitHub CLI"));
    }),
    new(50, "click Installed on the tab strip", () => screen.Click(TabStripX("Installed"), 1), Verify: () =>
        Check("Installed keys", ScreenHas(" x Uninstall "))),

    new(50, "3, clear the filter, filter Azure", () => { screen.Press(new Key('3')); screen.Press(new Key('/')); screen.Press(Key.Esc); screen.Press(new Key('/')); screen.Type("Azure"); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("batch keys on the Updates bar", ScreenHas(" u Upgrade   ␣ Mark   a Mark all   c Clear   g Run   p Policy   r Refresh   ? Help   q Quit "));
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
        Check("details pane gone", !ScreenHas(" Policy     "));
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
    new(50, "filter Docker, p, Down, Enter: hold it", () => { screen.Press(new Key('/')); screen.Type("Docker"); screen.Press(Key.Enter); screen.Press(Key.P); screen.Press(Key.CursorDown); screen.Press(Key.Enter); }),
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
        Check("details pane back", ScreenHas(" Policy     "));
        Check("count without marks", ScreenHas("15 available · 1 held"));
    }),
    new(50, "g with an empty queue", () => screen.Press(Key.G), Verify: () =>
        Check("empty-queue message", ScreenHas(Shell.QueueEmptyText))),

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
    new(50, "search VisualStudioCode", () => { screen.Press(new Key('/')); screen.Press(Key.Backspace, 3); screen.Type("VisualStudioCode"); screen.Press(Key.Enter); }),
    new(600, "Ctrl+Home, Space: VS Code, whose show says inno, marked for install", () => { screen.Press(Key.Home.WithCtrl); screen.Press(Key.Space); }),
    new(400, "after the details load: VS Code needs admin", () => { }, WithColors: true, Verify: () =>
    {
        Check("VS Code queued second", ScreenHas(" 2  Microsoft.VisualStudioCode"));
        Check("its plan needs elevation", shell.Queue.Items.Any(item => item.Row.Id == "Microsoft.VisualStudioCode" && item.Plan.RequiresElevation));
        Check("⚡ admin on its entry", RowWith("install → latest").Contains('⚡') && RowWith("install → latest").Contains(" admin"));
        Check("one of two", ScreenHas(" 1 of 2 need elevation.") && ScreenHas(" One UAC prompt will be shown."));
    }),
    new(50, "search git again", () => { screen.Press(new Key('/')); screen.Press(Key.Backspace, "VisualStudioCode".Length); screen.Type("git"); screen.Press(Key.Enter); }),
    new(600, "the git results are back", () => { }, Verify: () =>
        Check("Git.Git listed", LeftPaneHas("Git.Git"))),

    new(50, "c, 1, filter AutoHotkey, o: the install options editor", () =>
    {
        screen.Press(Key.C);
        screen.Press(new Key('1'));
        screen.Press(new Key('/'));
        screen.Press(Key.Esc);
        screen.Press(new Key('/'));
        screen.Type("AutoHotkey");
        screen.Press(Key.Enter);
        screen.Press(Key.O);
    }, Verify: () =>
    {
        Check("editor title", ScreenHas(" Install options  AutoHotkey.AutoHotkey · saved per package, used on every upgrade"));
        Check("scope default", ScreenHas(EditorLabel("Scope") + "(•) default  ( ) machine  ( ) user"));
        Check("architecture default", ScreenHas(EditorLabel("Architecture") + "(•) default  ( ) x64  ( ) arm64"));
        Check("version placeholder", ScreenHas(EditorLabel("Version to install") + "[ latest"));
        Check("flag rows", ScreenHas("[ ] Interactive install   [ ] Skip hash check   [ ] Pre-release")
            && ScreenHas("[ ] Run as administrator  [ ] Remove data on uninstall"));
        Check("command rows", ScreenHas(EditorLabel("Pre-install command") + "[ ") && ScreenHas(EditorLabel("Post-uninstall command") + "[ "));
        Check("updates row", ScreenHas(EditorLabel("Updates") + "[ ] Auto-update   [ ] Ignore all updates   Ignored version [ "));
        Check("footer", ScreenHas(" Stored in package-options.json · exported into bundles as InstallationOptions"));
        Check("editor key bar", ScreenHas(" ⏎ Save   Esc Cancel   Tab Next field   ␣ Toggle   Ctrl+R Reset   ? Help   q Quit "));
        Check("table hidden", !ScreenHas("Filter:"));
        Check("scope has focus", Focused() == nameof(OptionRow));
    }),
    new(50, "Right, Space: machine scope", () => { screen.Press(Key.CursorRight); screen.Press(Key.Space); }, Verify: () =>
        Check("machine picked", ScreenHas("( ) default  (•) machine  ( ) user"))),
    new(50, "click Pre-release: checked and focused", () => screen.Click(1 + 71 + 1, 3 + 5), Verify: () =>
    {
        Check("pre-release checked", ScreenHas("[x] Pre-release"));
        Check("checkbox has focus", Focused() == nameof(CheckField));
    }),
    new(50, "Space: unchecked again", () => screen.Press(Key.Space), Verify: () =>
        Check("pre-release unchecked", ScreenHas("[ ] Pre-release"))),
    new(50, "Enter: saved", () => screen.Press(Key.Enter), Verify: () =>
    {
        Check("saved message", ScreenHas("Saved options for AutoHotkey.AutoHotkey"));
        Check("table back", ScreenHas("Filter:"));
        var saved = File.ReadAllText(Path.Combine(dataDirectory, "package-options.json"));
        Check("scope in package-options.json", saved.Contains("\"InstallationScope\": \"machine\"", StringComparison.Ordinal));
    }),
    new(600, "details show custom options", () => { }, Verify: () =>
        Check("options row", ScreenHas(" Options    custom (o to edit)"))),
    new(50, "o again: machine is picked", () => screen.Press(Key.O), WithColors: true, Verify: () =>
        Check("machine picked", ScreenHas("( ) default  (•) machine  ( ) user"))),
    new(50, "Tab x2, type a version, Esc: asks first", () => { screen.Press(Key.Tab, 2); screen.Type("2.0.1"); screen.Press(Key.Esc); }, Verify: () =>
    {
        Check("typed into the version box", ScreenHas("[ 2.0.1"));
        Check("discard question", ScreenHas("Discard changes to AutoHotkey.AutoHotkey? (y/n)"));
    }),
    new(50, "n, Ctrl+R: reset to defaults", () => { screen.Press(Key.N); screen.Press(Key.R.WithCtrl); }, Verify: () =>
    {
        Check("still open", ScreenHas(" Install options  AutoHotkey.AutoHotkey"));
        Check("scope back to default", ScreenHas("(•) default  ( ) machine  ( ) user"));
        Check("version cleared", !ScreenHas("[ 2.0.1"));
    }),
    new(50, "Esc, y: discarded", () => { screen.Press(Key.Esc); screen.Press(Key.Y); }, Verify: () =>
    {
        Check("table back", ScreenHas("Filter:"));
        Check("still machine on disk", shell.Options.GetInstallOptions("AutoHotkey.AutoHotkey").InstallationScope == "machine");
    }),

    new(50, "3, filter OhMyPosh, p: the update policy dialog", () =>
    {
        // A hash mismatch as the package's last operation, so the dialog suggests excluding it.
        new HistoryStore(historyDirectory).Append(
            DateTimeOffset.Now, "upgrade", PolicyId, "Oh My Posh",
            new Wingman.Core.Models.OperationResult(-1978335215, false, TimeSpan.Zero, ["Installer hash does not match."]), [], null);
        screen.Press(new Key('3'));
        screen.Press(new Key('/'));
        screen.Press(Key.Esc);
        screen.Press(new Key('/'));
        screen.Type("OhMyPosh");
        screen.Press(Key.Enter);
        screen.Press(Key.P);
    }, Verify: () =>
    {
        Check("dialog title", ScreenHas(" Update policy  " + PolicyId + " · installed 30.7.0.0 · available 31.3.0"));
        Check("update chosen", ScreenHas(" (•) Update with Wingman"));
        Check("skip level", ScreenHas(" ( ) Skip 31.3.0 only"));
        Check("exclude level", ScreenHas(" ( ) Exclude: this app updates itself"));
        Check("note box", ScreenHas(" Note    [ "));
    }),
    new(300, "the hash-mismatch suggestion", () => { }, WithColors: true, Verify: () =>
        Check("suggestion", ScreenHas(" Suggested: Exclude. This app may update itself."))),
    new(50, "Down x3, Tab, type a note, Enter: excluded", () =>
    {
        screen.Press(Key.CursorDown, 3);
        screen.Press(Key.Tab);
        screen.Type("updates itself");
        screen.Press(Key.Enter);
    }, Verify: () =>
    {
        Check("excluded message", ScreenHas("Excluded " + PolicyId + " from Wingman updates"));
        Check("row left Updates", !LeftPaneHas("Oh My Posh"));
        Check("count", ScreenHas(" · 1 excluded"));
        Check("footer", ScreenHas("⟳ 1 excluded"));
        Check("tab strip reads Updates 14", ScreenHas(" Updates 14 "));
        Check("note kept for the session", shell.PolicyNotes[PolicyId] == "updates itself");
    }),
    new(50, "e: the excluded list", () => screen.Press(Key.E), Verify: () =>
    {
        Check("list title", ScreenHas(" Excluded from Wingman"));
        Check("its entry", ScreenHas(" ⟳ " + PolicyId) && ScreenHas("   self-updating"));
        Check("hint", ScreenHas(" p change policy for the row"));
        Check("details hidden", !ScreenHas(" Policy     "));
    }),
    new(50, "e again: the details are back", () => screen.Press(Key.E), Verify: () =>
        Check("list gone", !ScreenHas(" Excluded from Wingman"))),
    new(50, "1, filter OhMyPosh: ⟳ on the Installed row", () =>
    {
        screen.Press(new Key('1'));
        screen.Press(new Key('/'));
        screen.Press(Key.Esc);
        screen.Press(new Key('/'));
        screen.Type("OhMyPosh");
        screen.Press(Key.Enter);
    }, Verify: () =>
    {
        Check("excluded marker", LeftPaneHas(" ⟳ Oh My Posh"));
        Check("Policy field", ScreenHas(" Policy     excluded"));
    }),
    new(50, "p: the dialog chooses Exclude", () => screen.Press(Key.P), Verify: () =>
    {
        Check("exclude chosen", ScreenHas(" (•) Exclude: this app updates itself"));
        Check("note back", ScreenHas("[ updates itself"));
    }),
    new(50, "Up x3, Enter: updates with Wingman again", () => { screen.Press(Key.CursorUp, 3); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("restored message", ScreenHas(PolicyId + " updates with Wingman"));
        Check("no marker", !LeftPaneHas("⟳"));
        Check("Policy field", ScreenHas(" Policy     update"));
    }),
    new(50, "3: the row is back in Updates", () => screen.Press(new Key('3')), Verify: () =>
    {
        Check("row back", LeftPaneHas("Oh My Posh"));
        Check("nothing excluded", !ScreenHas("excluded"));
    }),

    new(50, "2, search vendor", () => { screen.Press(new Key('2')); screen.Press(new Key('/')); screen.Press(Key.Backspace, 3); screen.Type("vendor"); screen.Press(Key.Enter); }),
    new(600, "i, y: one more batch, which fails", () => { screen.Press(Key.I); screen.Press(Key.Y); }, Verify: () =>
        Check("running title", ScreenHas(" Running batch  1 of 1"))),
    new(1500, "the install failed", () => { }, Verify: () =>
        Check("finished title", ScreenHas(" Batch finished  1 of 1 · 1 failed"))),
    new(50, "Enter, 4: History", () => { screen.Press(Key.Enter); screen.Press(new Key('4')); }),
    new(400, "History loaded", () => historyCount = new HistoryStore(historyDirectory).List().Count, WithColors: true, Verify: () =>
    {
        Check("count", ScreenHas($"{historyCount} operations"));
        Check("header", ScreenHas("When             Operation  Package"));
        Check("ok and failed rows", LeftPaneHas(" ok ") && LeftPaneHas(" failed "));
        Check("the newest entry is the batch", LeftPaneHas(" batch      1 operations"));
        Check("history key bar", ScreenHas(" ⏎ Open log   R Retry   o Options   / Filter   Del Forget   ? Help   q Quit "));
    }),
    new(50, "R on the batch entry: nothing to retry", () => screen.Press(new Key('R')), Verify: () =>
        Check("refusal", ScreenHas("Nothing to retry for a batch entry"))),
    new(50, "Down to the failed install", () =>
    {
        var entries = new HistoryStore(historyDirectory).List().ToList();
        var index = entries.FindIndex(entry => entry.Operation == "install" && entry.ExitCode == 1603);
        forgotten = entries[index];
        screen.Press(Key.CursorDown, index);
    }, WithColors: true, Verify: () =>
    {
        Check("title", ScreenHas(" install " + SlowClient.FailingId));
        Check("exit code", ScreenHas(" exit code 1603  "));
        Check("usually means", ScreenHas(" Usually means "));
        Check("suggestion", ScreenHas(" Suggestion "));
        Check("its log", ScreenHas(" $ winget install"));
        Check("row keys", ScreenHas(" R retry   o edit options"));
    }),
    new(50, "Tab, PageDown: the log scrolls", () => { before = screen.Rows(); screen.Press(Key.Tab); screen.Press(Key.PageDown); }, Verify: () =>
    {
        Check("log pane has focus", Focused() == nameof(HistoryDetailsPane));
        Check("log moved", RightPaneText(screen.Rows()) != RightPaneText(before));
    }),
    new(50, "Tab back, Del: the forget question", () => { screen.Press(Key.Tab); screen.Press(Key.Delete); }, Verify: () =>
        Check("question", ScreenHas("Forget install " + SlowClient.FailingId + "? (y/n)"))),
    new(50, "y: forgotten", () => screen.Press(Key.Y)),
    new(300, "the list reloaded without it", () => { }, Verify: () =>
    {
        Check("count dropped by one", ScreenHas($"{historyCount - 1} operations"));
        Check("json gone", !File.Exists(Path.Combine(historyDirectory, Path.ChangeExtension(forgotten!.LogFileName, ".json"))));
        Check("log gone", !File.Exists(Path.Combine(historyDirectory, forgotten.LogFileName)));
        Check("forgot message", ScreenHas("Forgot install " + SlowClient.FailingId));
    }),
    new(50, "o: the install options editor takes the History tab", () => screen.Press(Key.O), Verify: () =>
    {
        Check("editor title", ScreenHas(" Install options  " + PolicyId));
        Check("list hidden", !ScreenHas("operations"));
    }),
    new(50, "Esc: the list is back", () => screen.Press(Key.Esc), Verify: () =>
    {
        Check("list back", ScreenHas($"{historyCount - 1} operations"));
        Check("table has focus", Focused() == "KeyPassingTableView");
    }),
    new(50, "R, y: retry the upgrade as a batch of one on the History tab", () => { screen.Press(new Key('R')); screen.Press(Key.Y); }, Verify: () =>
        Check("running title", ScreenHas(" Running batch  1 of 1") && ScreenHas(" upgrade  30.7.0.0 → 31.3.0"))),
    new(1000, "the retry finished", () => { }, Verify: () =>
        Check("finished title", ScreenHas(" Batch finished  1 of 1"))),
    new(50, "Enter: History reloaded with the retry and its batch", () => screen.Press(Key.Enter), Verify: () =>
    {
        historyCount = new HistoryStore(historyDirectory).List().Count;
        Check("count", ScreenHas($"{historyCount} operations"));
        var retried = new HistoryStore(historyDirectory).List().First(entry => entry.Operation != "batch");
        Check("the retry recorded", retried.PackageId == PolicyId && retried.Operation == "upgrade" && retried.Succeeded);
        Check("the retry listed", LeftPaneHas(" upgrade    JanDeDobbel"));
    }),

    new(50, "5: Settings", () => screen.Press(new Key('5')), WithColors: true, Verify: () =>
    {
        Check("defaults", ScreenHas(" Defaults") && ScreenHas("   Install scope             (•) default  ( ) user  ( ) machine"));
        Check("default flags", ScreenHas("[x] Accept package agreements   [x] Include unknown versions"));
        Check("elevation radio and continue on failure", ScreenHas("   " + "Elevation".PadRight(26) + "(•) Auto  ( ) Always  ( ) Never   [x] Continue on failure"));
        Check("restart action", ScreenHas("   ⏎ Restart as administrator") && !ScreenHas("Windows only") && !ScreenHas("already administrator"));
        Check("theme row", ScreenHas("   Theme                     (•) Midnight  ( ) Daylight  ( ) Nord  ( ) Dracula  ( ) Auto"));
        Check("phase 3 groups", ScreenHas(" Updates") && ScreenHas(" Tray") && ScreenHas("phase 3"));
        Check("tools", ScreenHas("   ⏎ Import bundle…   ⏎ Export bundle…   ⏎ Register scheduled tasks"));
        Check("footer", ScreenHas(" Saved to "));
        Check("settings key bar", ScreenHas(" Tab Next field   ␣ Toggle   ⏎ Activate   ? Help   q Quit "));
        Check("scope has focus", Focused() == nameof(OptionRow));
    }),
    new(50, "Tab x4, Space: Continue on failure off", () => { screen.Press(Key.Tab, 4); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("unchecked", ScreenHas("[ ] Continue on failure"));
        Check("saved", File.ReadAllText(settingsStore.FilePath).Contains("\"continueOnFailure\": false", StringComparison.Ordinal));
        Check("in effect", !shell.Settings.ContinueOnFailure);
    }),
    new(50, "Shift+Tab, Right x2, Space: elevation Never", () => { screen.Press(Key.Tab.WithShift); screen.Press(Key.CursorRight, 2); screen.Press(Key.Space); }, Verify: () =>
    {
        Check("Never picked", ScreenHas("( ) Auto  ( ) Always  (•) Never"));
        Check("saved", File.ReadAllText(settingsStore.FilePath).Contains("\"elevationMode\": \"never\"", StringComparison.Ordinal));
        Check("in effect", shell.Settings.ElevationMode == ElevationMode.Never);
    }),
    new(50, "3, filter Azure, Space on Azd: the queue pane says elevation is off", () =>
    {
        screen.Press(new Key('3'));
        screen.Press(new Key('/'));
        screen.Press(Key.Esc);
        screen.Press(new Key('/'));
        screen.Type("Azure");
        screen.Press(Key.Enter);
        screen.Press(Key.Home.WithCtrl);
        screen.Press(Key.Space);
    }, Verify: () =>
    {
        Check("Azd, which needs admin, has no ⚡ under Never", shell.Queue.Contains(ElevatedId) && !ScreenHas("⚡"));
        Check("queued", shell.Queue.Count == 1);
        Check("elevation off line", RightPaneFlowed().Contains("elevation off (winget will prompt per installer)", StringComparison.Ordinal));
        Check("no UAC sentence", !ScreenHas("One UAC prompt"));
    }),
    new(50, "c, 5, Left x2, Space, Tab: Auto again, focus back on Continue on failure", () =>
    {
        screen.Press(Key.C);
        screen.Press(new Key('5'));
        screen.Press(Key.CursorLeft, 2);
        screen.Press(Key.Space);
        screen.Press(Key.Tab);
    }, Verify: () =>
    {
        Check("queue empty", shell.Queue.Count == 0);
        Check("Auto picked", ScreenHas("(•) Auto  ( ) Always  ( ) Never"));
        Check("saved", File.ReadAllText(settingsStore.FilePath).Contains("\"elevationMode\": \"auto\"", StringComparison.Ordinal));
        Check("continue on failure has focus", Focused() == nameof(CheckField));
    }),
    new(50, "Tab x2, Enter on Import bundle…: the import screen takes the Settings tab", () => { screen.Press(Key.Tab, 2); screen.Press(Key.Enter); }, Verify: () =>
    {
        Check("import title", ScreenHas(" Import bundle  choose a .ubundle file"));
        Check("settings hidden", !ScreenHas(" Defaults"));
        Check("the last bundle offered", shell.LastBundlePath == bundleCopy);
    }),
    new(50, "Esc: the settings are back", () => screen.Press(Key.Esc), Verify: () =>
        Check("settings back", ScreenHas(" Defaults") && ScreenHas(" Tab Next field   ␣ Toggle   ⏎ Activate   ? Help   q Quit "))),
    new(50, "click Restart as administrator: the question", () => ClickText("⏎ Restart as administrator"), Verify: () =>
        Check("question", ScreenHas(Shell.RestartQuestionText))),
    new(50, "y: the restart is declined and Wingman keeps running", () => screen.Press(Key.Y), Verify: () =>
    {
        Check("restart asked once with this run's arguments", restartRequests.Count == 1
            && restartRequests[0].SequenceEqual(Environment.GetCommandLineArgs().Skip(1)));
        Check("declined status", ScreenHas(Shell.RestartDeclinedText));
        Check("still running", shell.Window.IsRunning);
    }),
    new(50, "click Daylight: the theme switches live", () => ClickText("( ) Daylight"), WithColors: true, Verify: () =>
    {
        Check("Daylight picked", ScreenHas("(•) Daylight"));
        Check("saved", File.ReadAllText(settingsStore.FilePath).Contains("\"theme\": \"Daylight\"", StringComparison.Ordinal));
        Check("Daylight ground", screen.BackgroundAt(width / 2, height / 2) == Theme.Daylight.Background);
        Check("no Midnight ground left", !screen.AnyBackground(Theme.Midnight.Background));
    }),
    new(50, "4: the History table is in Daylight too", () => screen.Press(new Key('4')), WithColors: true, Verify: () =>
    {
        var daylightRows = Enumerable.Range(FirstRowY, 4).Count(y => screen.BackgroundAt(3, y) == Theme.Daylight.Background);
        Check("table cells on the Daylight ground", daylightRows >= 3);
        Check("header on the Daylight ground", screen.BackgroundAt(3, FirstRowY - 1) == Theme.Daylight.Background);
        Check("no Midnight ground left", !screen.AnyBackground(Theme.Midnight.Background));
    }),
    new(50, "5, Right, Space: Nord", () => { screen.Press(new Key('5')); screen.Press(Key.CursorRight); screen.Press(Key.Space); }, WithColors: true, Verify: () =>
    {
        Check("Nord picked", ScreenHas("(•) Nord"));
        Check("Nord ground", screen.BackgroundAt(width / 2, height / 2) == Theme.Nord.Background);
    }),
    new(50, "Right, Space: Dracula", () => { screen.Press(Key.CursorRight); screen.Press(Key.Space); }, WithColors: true, Verify: () =>
    {
        Check("Dracula picked", ScreenHas("(•) Dracula"));
        Check("Dracula ground", screen.BackgroundAt(width / 2, height / 2) == Theme.Dracula.Background);
    }),
    new(50, "click Midnight: back to the default", () => ClickText("( ) Midnight"), Verify: () =>
    {
        Check("Midnight picked", ScreenHas("(•) Midnight"));
        Check("Midnight ground", screen.BackgroundAt(width / 2, height / 2) == Theme.Midnight.Background);
        Check("saved", File.ReadAllText(settingsStore.FilePath).Contains("\"theme\": \"Midnight\"", StringComparison.Ordinal));
    }),

    new(50, "3, clear the filter, Ctrl+Home, m: Upgrade to version… in the menu", () =>
    {
        screen.Press(new Key('3'));
        screen.Press(new Key('/'));
        screen.Press(Key.Esc);
        screen.Press(Key.Home.WithCtrl);
        screen.Press(Key.M);
    }, Verify: () =>
    {
        var rows = screen.Rows().ToList();
        var upgradeY = rows.FindIndex(row => row.Contains("│ Upgrade to ", StringComparison.Ordinal));
        Check("version entry right after the upgrade", upgradeY >= 0 && rows[upgradeY + 1].Contains("│ Upgrade to version… ", StringComparison.Ordinal));
        Check("options and policy entries", ScreenHas("│ Install options… ") && ScreenHas("│ Update policy… "));
    }),
    new(50, "Down, Enter: the version picker", () => { screen.Press(Key.CursorDown); screen.Press(Key.Enter); }, Verify: () =>
        Check("the menu gave way to the picker", !IsMenuOpen() && (ScreenHas("│ loading… ") || ScreenHas(" 1 version ")))),
    new(200, "the picker lists the one version the fake knows", () => { }, WithColors: true, Verify: () =>
        Check("one version", ScreenHas(" 1 version "))),
    new(50, "Esc: the picker closed", () => screen.Press(Key.Esc), Verify: () =>
    {
        Check("picker closed", !ScreenHas(" 1 version "));
        Check("no question", !ScreenHas("(y/n)"));
    }),
    new(50, "2, search Git.Git", () => { screen.Press(new Key('2')); screen.Press(new Key('/')); screen.Press(Key.Backspace, 20); screen.Type("Git.Git"); screen.Press(Key.Enter); }),
    new(600, "m, Down x2, Enter on Install version…", () => { screen.Press(Key.M); screen.Press(Key.CursorDown, 2); screen.Press(Key.Enter); }),
    new(200, "the picker lists Git's versions, newest first", () => { }, WithColors: true, Verify: () =>
    {
        Check("installed version first and marked", ScreenHas("│ 2.55.0.3  installed"));
        Check("second entry", ScreenHas("│ 2.55.0.2 "));
        Check("ten shown, the count on the border", screen.Rows().Count(row => row.Contains("│ 2.", StringComparison.Ordinal)) == 10 && ScreenHas(" versions "));
    }),
    new(50, "Down, Enter: the install question for the second version", () => { screen.Press(Key.CursorDown); screen.Press(Key.Enter); }, Verify: () =>
        Check("question", ScreenHas("Install Git.Git 2.55.0.2? (y/n)"))),
    new(50, "y: a batch of one pinned to that version", () => screen.Press(Key.Y), Verify: () =>
    {
        Check("running title", ScreenHas(" Running batch  1 of 1"));
        Check("pinned version", BatchRow("Git.Git").Contains("install  → 2.55.0.2"));
    }),
    new(1000, "the install finished at that version", () => { }, Verify: () =>
    {
        Check("finished title", ScreenHas(" Batch finished  1 of 1"));
        Check("the log names the version", ScreenHas("Found Git [Git.Git] Version 2.55.0.2"));
        var install = new HistoryStore(historyDirectory).List().First(entry => entry.Operation == "install" && entry.PackageId == "Git.Git");
        Check("winget got --version", install.Arguments.Contains("--version") && install.Arguments.Contains("2.55.0.2"));
    }),
    new(50, "Enter: the results are back", () => screen.Press(Key.Enter), Verify: () =>
        Check("results back", ScreenHas("Search:"))),

    new(50, "1, o: the editor has every command field", () => { screen.Press(new Key('1')); screen.Press(Key.O); }, WithColors: true, Verify: () =>
    {
        Check("editor title", ScreenHas(" Install options  " + PolicyId));
        Check("pre-update", ScreenHas(EditorLabel("Pre-update command") + "[ "));
        Check("post-update", ScreenHas(EditorLabel("Post-update command") + "[ "));
        Check("pre-uninstall", ScreenHas(EditorLabel("Pre-uninstall command") + "[ "));
        Check("post-uninstall", ScreenHas(EditorLabel("Post-uninstall command") + "[ "));
        Check("abort checkbox", ScreenHas(EditorLabel("") + "[ ] Abort on pre-command failure"));
        Check("kill and updates rows still fit", ScreenHas(EditorLabel("Kill before operation") + "[ ")
            && ScreenHas(EditorLabel("Updates") + "[ ] Auto-update") && ScreenHas("Ignored version [ " + new string(' ', 8) + " ]"));
        Check("footer still fits", ScreenHas(" Stored in package-options.json · exported into bundles as InstallationOptions"));
    }),
    new(50, "Right, Space: machine scope, then q: the discard question", () => { screen.Press(Key.CursorRight); screen.Press(Key.Space); screen.Press(Key.Q); }, Verify: () =>
    {
        Check("discard question", ScreenHas(Shell.DiscardAndQuitText));
        Check("still running", shell.Window.IsRunning);
    }),
    new(50, "n: the editor stays open", () => screen.Press(Key.N), Verify: () =>
    {
        Check("still open", ScreenHas(" Install options  " + PolicyId));
        Check("machine still picked", ScreenHas("( ) default  (•) machine  ( ) user"));
        Check("still running", shell.Window.IsRunning);
    }),
    new(50, "Esc, y: discarded", () => { screen.Press(Key.Esc); screen.Press(Key.Y); }, Verify: () =>
    {
        Check("table back", ScreenHas("Filter:"));
        Check("nothing saved", shell.Options.GetInstallOptions(PolicyId).IsDefault());
    }),

    new(50, "q quits", () => screen.Press(Key.Q)),
];

var steps = isElevatedRun ? elevatedSteps : mainSteps;
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
app.Dispose();

try
{
    Directory.Delete(dataDirectory, recursive: true);
}
catch (IOException)
{
}

var runName = isElevatedRun ? "elevated run" : "main run";
screen.Log($"{runName} exited normally, {failedChecks} failed checks");
Console.WriteLine($"{runName}: wrote {screen.OutputPath}, {failedChecks} failed checks");

if (isElevatedRun)
{
    return failedChecks == 0 ? 0 : 1;
}

var elevatedRun = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };

// Under `dotnet TuiHarness.dll` the process is dotnet itself, which needs the assembly first.
if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
{
    elevatedRun.ArgumentList.Add(typeof(Screen).Assembly.Location);
}

foreach (var arg in args.Append(ElevatedFlag))
{
    elevatedRun.ArgumentList.Add(arg);
}

using var elevatedProcess = Process.Start(elevatedRun)!;
elevatedProcess.WaitForExit();
var elevatedPassed = elevatedProcess.ExitCode == 0;
screen.Log($"check {(elevatedPassed ? "ok" : "FAILED")}: the elevated run exited with {elevatedProcess.ExitCode}");
return failedChecks == 0 && elevatedPassed ? 0 : 1;

internal sealed record Step(int DelayMs, string Label, Action Act, bool WithColors = false, Action? Verify = null);
