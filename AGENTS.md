# Wingman

A terminal UI for winget, in C# with Terminal.Gui, with UniGetUI bundle compatibility.
Read `docs/DESIGN.md` before doing anything; it holds every product decision, the
architecture, and the phase plan. `docs/mockups/wingman-screens.html` shows every screen.

## How work is organized

All planning lives in GitHub Issues on `BenBurge/Wingman`. There is no other backlog.

- Milestones are the phases: "Phase 1: usable TUI", "Phase 2: batches, options,
  bundles", "Phase 3: automation". Work phase 1 to completion before phase 2.
- Each issue is one change Ben can review in under fifteen minutes. Do not merge
  several issues into one branch, and do not start work that has no issue. If a task
  needs an issue that does not exist, create it first with the same labels and
  milestone as its neighbors and say so in the PR.
- Pick the lowest-numbered open issue in the current milestone that nothing blocks.
  Issues labeled `needs-fixtures` are blocked until captured winget output exists in
  `tests/Wingman.Core.Tests/Fixtures/`.
- Branch from `main` as `issue-<number>-<short-slug>`. One PR per issue. The PR title
  starts with the issue number, the body starts with `Closes #<number>`, and it ends
  with a `## How to review` section that says exactly what to run or look at. Copy the
  issue's own "How to review" text and refine it with what you actually built.
- Ben reviews and merges every PR himself. Do not merge, do not close issues, and do
  not push to `main` directly.
- Before opening a PR: `dotnet build` and `dotnet test` must pass with zero warnings.
  For TUI issues, also run `dotnet run --project src/Wingman -- --fake` and confirm the
  screen matches the mockup for that issue. Say in the PR what you verified and how.
- Ben can suspend the one-PR-per-issue rule for a stretch of work; then commit per
  issue on an integration branch and open one PR for the batch, still one issue per
  commit with the issue number in the message.

## Conventions

- Build with `dotnet build`, test with `dotnet test`. .NET 10, Terminal.Gui 2.5.
- `Wingman.Core` must stay free of Windows-only APIs and of any reference to
  Terminal.Gui. It is the cross-platform layer and must build and test on macOS and
  Linux as well as Windows.
- `Wingman.Cli` is the headless command layer and also stays cross-platform; it reaches Windows-only services (`ISetupExecutor`, `IToastSender`, `ISelfUpdateStarter`, the elevation factory, the tray) only through interfaces defined in Core that `src/Wingman/Program.cs` wires from `Wingman.Windows.HostServices` and `ElevationSupport`. Tests build a `CliContext` with `FakeWingetClient` and `StringWriter`s; see `tests/Wingman.Core.Tests/CliHarness.cs`.
- Never call `winget` from tests or at build time. `IWingetClient` and
  `IProcessRunner` exist so tests run against fixtures captured on Windows with
  `tools/Capture-WingetFixtures.ps1`. Parser changes must keep fixture tests green.
- Per-package install options mirror UniGetUI's `InstallOptions` / `UpdatesOptions`
  schema verbatim so bundles stay interchangeable with UniGetUI. Do not rename those
  properties, and do not drop the ones Wingman does not use.
- Terminal.Gui 2.5 uses the instance API: `Application.Create()`, `app.Init()`,
  `app.Run(...)`. The static `Application.Init/Run/Shutdown` members are obsolete and
  fail the build because warnings are errors.
- Keep the UI keyboard-first and fully mouse-operable. Every action has a key shown in
  the status bar and is also reachable by click or the row context menu.
- American English everywhere. Comments explain why, never what changed.

## Terminal.Gui 2.5 gotchas

- Instance API only: `using var app = Application.Create(); app.Init(); app.Run(window);`. Marshal background results to the UI thread with `app.Invoke(Action)`. Timers are `app.AddTimeout(TimeSpan, Func<bool>)` / `RemoveTimeout`.
- Look up members in `%USERPROFILE%\.nuget\packages\terminal.gui\2.5.0\lib\net10.0\Terminal.Gui.xml`; v1 names are mostly gone.
- Custom drawing: override `OnDrawingContent(DrawContext?)` and use `SetAttribute`, `Move`, `AddStr`. Mouse: override `OnMouseEvent(Mouse)`; `mouse.Position` is view-relative. A fast second click arrives as `LeftButtonDoubleClicked`, not a second `Clicked`; `MouseExtensions.IsLeftClick()` accepts all three click flags.
- Colors: `Scheme` is a record; set `Normal`, `Focus`, `Active`, `Highlight`, `HotNormal`, `Editable`. The border color is set through `Window.Border.GetOrCreateView().SetScheme(...)`. `TextField` text uses `Editable`. `TableView` headers use `Style.HeaderScheme`; set every role or the header renders inverted.
- Writing on the top border (title left, winget version right): turn off the built-in title with `Window.Border.Settings &= ~BorderSettings.Title`, then draw in `Window.DrawComplete` inside `SetClipToScreen()` / `SetClip(saved)`.
- Separator lines that join the border as `├ ┬ ┴ ┤`: `new Line { X = -1, Width = Dim.Fill(-1), SuperViewRendersLineCanvas = true }`; every container between the line and the window must also set `SuperViewRendersLineCanvas = true`.
- `TableView`: selection is `Value` / `ValueChanged` with `TableSelection`; Enter and double-click raise `Accepting` (set `e.Handled = true`); set `CollectionNavigator = null` or type-to-search eats letter keys; set `Style.AlwaysShowHeaders = true`; fixed widths are `ColumnStyle.MinWidth = MaxWidth` with `RepresentationGetter` truncating; call `Update()` after changing column styles; with zero rows it ignores widths, so pad header text; `ScreenToCell(pos, out int? header)` detects header clicks; it does not wheel-scroll by default, so move `RowOffset` yourself; arrow keys at the ends bubble out, so stop them in `KeyDownNotHandled`.
- Key routing: the focused view sees a key first (`KeyDown`, bindings, `KeyDownNotHandled`), then parents. Handle global keys in `Window.KeyDown` and return early when `MostFocused is TextField`.
- `StatusBar` always draws `│` separators and pads items, which overflows at 96 columns; the shell uses the custom `KeyBar` instead.
- Show the first tab from `Window.IsRunningChanged`; focus and timers need the running app. Pane widths are set in `OnSubViewLayout`; a `Dim.Func` reading the parent's size can see the previous frame.
- `tools/TuiHarness` compiles the Tui sources into a console app on the ANSI driver at a fixed size, scripts keys and mouse events, and dumps every screen cell to `out.txt`. Run `dotnet run --project tools/TuiHarness -- 96 30 Midnight` and read the frames; use it to check any layout change, since nothing else can see the screen.
- An empty `TableView` with focus returns true from `OnKeyDownNotHandled` for every printable key, even with `CollectionNavigator = null`, so `1`-`5`, `q`, and the key bar go dead. `PackageTable` uses a nested `TableView` subclass that returns false when there are no rows.
- `Home` and `End` in `TableView` move between columns, which does nothing with `FullRowSelect`; `Ctrl+Home` and `Ctrl+End` move between rows.
- Per-cell colors come from `ColumnStyle.ColorGetter`, which returns a `Scheme`. The cursor row draws with that scheme's `Focus` (or `Active` when unfocused), so a marker scheme must set both to the selected colors; `Theme.CellScheme(color)` does this.
- Sibling views overlap in add order: a view added later draws over an earlier one. Add centered overlay labels after the `TableView`.
- In Git Bash, `grep -c $'\r'` miscounts carriage returns; use `tr -cd '\r' < file | wc -c` to check line endings.
- `app.Keyboard.KeyDown` fires before any view, including the focused table; the y/n confirmation prompt in `Shell` uses it to take every key while a question is up.
- Reloading rows after an operation that removed the cursor row fires `CursorChanged`; treat cursor moves during `SetRows` as data changes, not user actions, if a feature ever depends on the difference.
- `TableStyle.RowColorGetter` colors a whole row, but a column's own `ColorGetter` wins for that column.
- A `Label` clips overflowing text without an ellipsis; fit it with `CellText.Fit` first.
- `tools/TuiHarness` steps can assert on their frame and the harness exits 1 on any failed check, so run it after every TUI change.
- The window's line canvas (pane divider, separators) draws after every subview, so an overlay added as a normal view gets cut through. `ContextMenu` and `HelpOverlay` are painted from `Window.DrawComplete` inside `SetClipToScreen()` / `SetClip(saved)`.
- `App.Mouse.MouseEvent` fires before any view; setting `Handled` there stops the event. Swallow the press and release too, or the table moves its cursor on a click outside an overlay.
- `IsSingleDoubleOrTripleClicked` also covers right clicks.
- The harness's ANSI driver sometimes resets its size between steps, which breaks mouse coordinates; `Screen.HoldSize()` restores it on `IDriver.SizeChanged`. The footer separator's left end is sometimes drawn `│` instead of `├` between steps (a Terminal.Gui glitch), so the harness leaves separator rows out of comparisons.
- `TableView` binds Space to its multi-select toggle and consumes the key; `PackageTable`'s nested `TableView` subclass removes that binding so Space reaches the key bar.
- Terminal.Gui measures glyph width with `GetColumns()`, which can disagree with Core's `DisplayWidth` for emoji-presentation symbols; keep `DisplayWidth`'s wide ranges in sync when a new symbol is drawn, and measure UI text with `GetColumns()` when the two must agree on screen.
- The harness dumps the second cell of a wide character as a space, so frame checks must not expect `⚡ admin` as one contiguous string.
- Python cannot open files under the long scratchpad path (over 260 characters); pipe scripts through stdin or use a short temp path.
- `WINGMAN_DATA_DIR` overrides the settings, package-options, and history folders; the harness sets it to a temp folder so runs never touch the real profile.
- `BatchRunner` does not return the history entries it writes; the TUI finds an operation's `.log` afterward by listing history and matching `BatchId`, verb, and `PackageId`.
- On Windows `Task.Delay(40)` takes about 46 ms, so fake operations run longer than steps × delay; harness timings must allow for it.
- `BatchRunnerScreen` replaces the whole content area of a tab (table and right pane hidden, tab strip and key bar kept); editors and dialogs follow the same pattern rather than opening modal `Dialog`s.
- There is no `RadioGroup` in 2.5; the built-in `OptionSelector` and `CheckBox` (`Value`, not `CheckedState`) draw global glyphs, so `FormFields` draws radio rows and checkboxes by hand in theme colors.
- `TextField` has no placeholder; `FormTextField` draws one in `OnDrawingContent` when empty and unfocused. `OnKeyDown` is the earliest hook for Tab, Enter, and Esc, ahead of the field's own bindings.
- `IApplication.Begin` installs a `MainLoopSyncContext`, so an `await` started on the UI thread resumes on the UI thread; `Shell.ApplyPolicyAsync` relies on this to keep the stores single-threaded.
- `Key.R.ToString()` is `r`; use `KeyLabel` to show an uppercase letter, and remember `new Key('E')` is Shift+E and does not match a `Key.E` hint.
- Harness checks next to an overlay should cover only the box, because rows under it change while a batch streams.
- `python3` is not installed on the development machine; use PowerShell or a small C# script for scripted checks.
- A view that draws with theme colors must implement `IThemedView`, or it keeps the old palette after a switch. Cell and row color getters take the `Theme` and read it at draw time rather than holding a `Scheme` built earlier.
- `View.Activated` already exists, so an event named `Activated` on a subclass fails with CS0108; `ActionField` uses `Pressed`.
- `Pos.GetAnchor` and `Dim.GetAnchor` are internal; compute field widths yourself (`CheckField.WidthFor`, `ActionField.WidthFor`).
- `SetFocus()` on a container gives focus back to the subview that had it last; the Settings tab relies on this.
- `HistoryEntry` has no canceled flag; `HistoryRow.FromEntries` infers it from the log (an operation whose log ends with `Canceled`, a batch that canceled something and failed nothing).
- `ScreenHostTab` is the base for any tab that can show the batch screen or a form in its content area; `Shell.RunOperation` takes the host so History can retry from its own tab.
- A `Label` word-wraps long text with no spaces onto rows you cannot see; fit paths yourself with `CellText.FitKeepingEnd` before putting them in the message line.
- The install options editor fills all 23 content rows at 96x30 and its Updates row ends at the last column; a new row needs the form to scroll and the label column cannot grow.
- The shell handles `m` (context menu) only after the key bar hints, so a screen can bind `m` to its own action.
- To retype a prefilled `FormTextField` in the harness, press End, then Backspace once per character.
- The clone uses `core.autocrlf=true`, so checked-out files are CRLF in the working tree while the repo stores LF; a "0 carriage returns" check on a file you did not touch is meaningless, and git normalizes on commit either way.
- A settings form that is hidden and shown again focuses its first field, not the last one; harness steps after that should click rather than Tab.
- The queue pane is 37 columns at 96x30, so its summary lines wrap; harness checks use `RightPaneFlowed()` to read them.
- `OperationPlan.RequiresElevation` means "needs admin rights at all" (resolved under `Auto`); the mode and the process's own elevation are applied later by `ElevationPolicy.UsesHelper`, so the batch runner can report `off` or `running as administrator` correctly.
- Background `winget show` lookups for queued rows run one at a time and only when the details are not cached and the plan does not already need elevation, so `a` on Updates does not spawn a winget process per row.
- A `TextField` exactly as wide as its text scrolls one cell to keep the cursor visible and stays scrolled after losing focus; set `InsertionPoint = 0` on leave.
- A harness step that presses a tab key and then uses `ClickText` in the same action reads the previous frame; switch tabs in one step and click in the next.
- In this Bash environment a heredoc turns a double backslash before a brace into a single backslash inside C# interpolated strings; use the Write or Edit tools for C# containing backslashes.

## CLI and Windows service gotchas

- Boolean command options must be listed in `ICliCommand.Flags`, or `CliArgs` treats the next token as the option's value (`upgrade --yes Git.Git` would swallow `Git.Git`).
- Under `--json`, the batch commands (`upgrade`, `install`, `import`) write plan and progress text to stderr so stdout holds only the JSON document.
- `wingman history` numbers operations only; `batch` entries are hidden, so `show <n>` and `forget <n>` use the same numbers as the list.
- `OperationPlan.RequiresElevation` is resolved under `Auto` with `processIsElevated: false`; the real mode and the process's own elevation are applied later by `ElevationPolicy.UsesHelper`, in the TUI and CLI alike.
- Toasts are shown by a hidden `powershell.exe` running `ToastBuilder.BuildPowerShellCommand`; the `BenBurge.Wingman` AppUserModelID is valid only after `wingman setup` has created the Start Menu shortcut carrying it.
- `SetupPlanner` emits two scheduled tasks for the check (`Wingman\Check` hourly and `Wingman\CheckAtLogon`) because `schtasks` cannot combine triggers on one command line.
- The tray is a message-only window (`HWND_MESSAGE`), so it never receives `TaskbarCreated`; it re-adds the icon when `NIM_MODIFY` fails and re-reads the taskbar theme every 60 seconds. With `NOTIFYICON_VERSION_4`, handle `NIN_SELECT` and `WM_CONTEXTMENU`, not the raw button messages, and set `NIF_SHOWTIP` or the tooltip stays hidden.
- `LibraryImport` needs `AllowUnsafeBlocks` and cannot marshal `NOTIFYICONDATAW`'s fixed-length strings; the tray uses `DllImport` with `DefaultDllImportSearchPaths(System32)`.
- A class must not share the simple name of its namespace (`Settings.Settings`, `SelfUpdate.SelfUpdate`): callers outside the namespace then resolve the name to the namespace and fail to compile.
- Icon assets ship as `Content` next to the exe under `assets/` and `assets/tray/`; `LinkBase` with a wildcard drops subfolders, so the two folders are separate items.
- `wingman.exe` is a console program: the startup entry and scheduled tasks wrap it in `conhost.exe --headless` and `wingman tray` calls `FreeConsole()` so no console window lingers. Anything else that launches Wingman in the background needs the same treatment.
- Setting `Console.OutputEncoding` throws `IOException` when there is no console; `Program.cs` catches it.
- Under the test host the entry assembly is `testhost`, so CLI tests set `CliContext.Version` instead of relying on `CliRunner.Version`.
- `ToastNotifier` writes failures to `Console.Error`, not the command's `Error` writer.
