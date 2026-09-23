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
- A tab's `OnShown` that calls `Table.FocusTable()` steals focus from a `LogPane` that is showing; check for a visible log first.
- Reloading rows after an operation that removed the cursor row fires `CursorChanged`. Code that treats a cursor move as a user action must ignore changes raised during `SetRows`.
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
