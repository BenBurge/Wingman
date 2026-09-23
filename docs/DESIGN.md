# Wingman design

Wingman is a keyboard-driven terminal UI for winget, plus a headless CLI, built in C# with Terminal.Gui. It targets Windows 10/11 with winget 1.4 or newer. It is developed on macOS, so every winget interaction sits behind an interface with a fixture-backed fake.

## Decisions

Views: Discover (search), Installed, Updates, each with a details pane, live filter, and sortable columns.
Operations: install, upgrade, uninstall; multi-select with a batch queue; version and architecture picking; scope, interactive, skip-hash, and custom-argument flags.
Per-package state: pins and ignored versions (backed by native `winget pin` where possible), remembered install options, pre/post commands, operation history with logs.
Elevation: one UAC prompt per batch through an elevated helper process.
Data: read and write UniGetUI bundles (`.ubundle`). Native `winget export` JSON, timestamped backups, and Gist sync are out of scope.
Automation: headless CLI, scheduled update checks and installs via Task Scheduler, toast notifications.
UI: themes with light/dark detection, config file with a settings tab. Keyboard and mouse are both first-class: every control responds to click, double-click, wheel, and right-click via Terminal.Gui's built-in mouse support. No Vim keys, no command preview.
Shell: system tray icon and a self-updater.
Stack: C# / .NET 10, Terminal.Gui 2.5, shells out to winget.exe, winget only (no other package managers).

## Architecture

```
src/Wingman.Core   models, IWingetClient + WingetCliClient, output parsers, bundle (de)serialization,
                   settings, package-options store, history store. No UI dependency. Cross-platform.
src/Wingman.Tui    Terminal.Gui views. Depends on Core only.
src/Wingman        the `wingman` executable. No args: launches the TUI. Subcommands: headless CLI.
src/Wingman.Windows elevated helper launcher and worker (phase 2); Task Scheduler registration,
                   toasts, tray icon (phase 3). Windows-only; referenced by src/Wingman only.
tests/Wingman.Core.Tests   xunit; parsers are tested against captured winget output in Fixtures/.
```

Dependency direction is strictly Core ← Tui ← Wingman. Windows-only code never lands in Core.

### Talking to winget

`WingetCliClient` runs `winget.exe` through `IProcessRunner` and parses stdout. Every call passes `--disable-interactivity --accept-source-agreements`. Reads (`list`, `search`, `upgrade`, `show`, `pin list`) are parsed by `WingetTableParser` (fixed-width tables keyed on header column offsets) and a key/value parser for `show`. Writes (`install`, `upgrade`, `uninstall`, `pin add/remove`) stream output line by line into the operation log and report the exit code.

Fixtures are captured on a real Windows machine with `tools/Capture-WingetFixtures.ps1` and committed under `tests/Wingman.Core.Tests/Fixtures/`. Parser changes must keep those tests green.

### Per-package options and UniGetUI compatibility

Wingman stores per-package install options using UniGetUI's `InstallOptions` schema verbatim, keyed by winget package id, in `package-options.json`. Because the on-disk model is the same one UniGetUI writes into bundles, exporting a bundle is a projection of the installed list plus that store, and importing one is a merge into it. Bundles are written with `export_version: 3` and `ManagerName: "WinGet"`. Packages from other managers in an imported bundle are listed as incompatible and skipped.

Ignore/hold maps to `winget pin add --blocking`; ignoring a single version is stored in `UpdatesOptions.IgnoredVersion` and filtered in the Updates view.

The update policy dialog (`p` on any row, also offered from a failed operation) has four levels: update with Wingman (default), hold at the current version (a blocking winget pin), skip one version (`UpdatesOptions.IgnoredVersion`), and exclude (`UpdatesOptions.UpdatesIgnored`), which is for apps that update themselves such as Discord or Chrome. Excluded packages leave the Updates tab, the auto-update task, and toast counts, but stay in Installed with a marker, and the Updates tab shows a one-line footer counting them. When an operation fails, Wingman decodes known winget and MSI error codes into a plain-language cause and offers retry, retry interactive, retry skipping the hash check, hold, and exclude.

### State on disk

`%APPDATA%\Wingman\` (Environment.SpecialFolder.ApplicationData on every OS):
- `settings.json`: defaults (scope, source, flags, theme), schedule settings.
- `package-options.json`: `Dictionary<string, InstallOptions>` plus `Dictionary<string, UpdatesOptions>`.
- `history/<timestamp>-<op>-<id>.json` and `.log`: one pair per operation, pruned to the last 1000.

### Batch queue and elevation

The TUI marks packages, then builds an ordered queue of operations. Operations that need elevation are sent to a single elevated helper (`wingman --elevated-worker <pipe-name>`) started once per batch with ShellExecute `runas`, communicating over a named pipe. Non-elevated operations run in-process. Progress and log lines flow back into the history store and the TUI log pane.

The TUI is the pipe server: it creates the pipe, starts the helper with `runas`, and waits for it to connect, so the helper needs nothing but the pipe name. Messages are newline-delimited JSON (`run`, `line`, `finished`, `shutdown`). The helper's read-run-reply loop lives in Core (`ElevatedWorkerLoop`) and is tested cross-platform against an in-process pipe; only the launcher and the process entry point are Windows-only. Declining the UAC prompt cancels the elevated operations in the batch and still runs the others.

### Bundle export and import

Both are reached with the `b` key on the Installed, Discover, and Updates tabs, and from the Tools row on the Settings tab. Export pre-selects every package winget can reinstall and lists the rest under `incompatible_packages`. Import shows a plan first (install, upgrade, keep, skip per row, with packages from other managers shown but unselectable), then hands the selected operations to the batch runner.

### Screens

Every list tab uses one widget: a filterable, sortable table on the left and a details pane on the right, with the tab strip and key bar fixed. The Updates tab title carries the available count. Held packages stay visible, dimmed with a hold marker. Marking rows builds a visible queue in the details pane, showing which operations need elevation. The batch runner replaces the content area rather than opening a modal. Right-clicking a row (or pressing `m`) opens a context menu with the row's actions.

### Distribution and background pieces

There is no installer and no Windows service. Wingman ships as a portable single-file executable published to the winget community repository, which puts it on the user's PATH. `wingman setup` idempotently registers the scheduled tasks (update check, auto-install), the login startup entry for the tray process, and the Start Menu shortcut with the AppUserModelID that unpackaged apps need for toasts; `wingman setup --remove` tears them down. Self-update runs `winget upgrade` for Wingman through a detached process, because a running executable cannot be overwritten.

Toasts are native Windows toasts and follow the system theme and accent. Actions deep-link into Wingman: "Update all" opens the batch runner with the queue prefilled, "View log" opens History on that row. At most one toast per check.

The tray icon is the W of Wingman whose last stroke lifts into a wing tip, drawn as a single 2-unit stroke on a 16-unit grid so it inherits the taskbar foreground color like the built-in tray icons. A bottom-right badge shows state: amber dot for updates available, amber ring for working, red dot for a failed last run, gray glyph when notifications are paused. Left-click opens Wingman on the Updates tab in a new terminal window; right-click shows a menu with update-all, open, check now, pause notifications, settings, and quit. The app icon used for toasts and the Start Menu shortcut is the same W on an amber (#F0B54A) tile with a dark (#171B26) glyph.

## Phases

1. Core client and parsers with fixtures; TUI with the three views, details pane, filter, sort, install/upgrade/uninstall/pin for one package at a time, output log pane, background operations.
2. Batch queue, elevated helper, per-package options editor, pre/post commands, bundle import/export, history tab, settings tab, themes, context menus.
3. Headless CLI (`check`, `list`, `upgrade`, `export`, `import`, `history`, `setup`), Task Scheduler registration, toasts, tray icon, self-updater.

## Testing strategy

Core is fully testable on macOS: parsers against fixtures, bundle round-trips, options store, queue ordering. The TUI is exercised manually on Windows. Every PR must keep `dotnet test` green on both OSes.
