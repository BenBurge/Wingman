# Wingman design

Wingman is a keyboard-driven terminal UI for winget, plus a headless CLI, built in C# with Terminal.Gui. It targets Windows 10/11 with winget 1.4 or newer. It is developed on macOS, so every winget interaction sits behind an interface with a fixture-backed fake.

## Decisions

Views: Discover (search), Installed, Updates, each with a details pane, live filter, and sortable columns.
Operations: install, upgrade, uninstall; multi-select with a batch queue; version and architecture picking; scope, interactive, skip-hash, and custom-argument flags.
Per-package state: pins and ignored versions (backed by native `winget pin` where possible), remembered install options, pre/post commands, operation history with logs.
Elevation: one UAC prompt per batch through an elevated helper process.
Data: read and write UniGetUI bundles (`.ubundle`). Native `winget export` JSON, timestamped backups, and Gist sync are out of scope.
Automation: headless CLI, scheduled update checks and installs via Task Scheduler, toast notifications.
UI: themes with light/dark detection, config file with a settings tab. No Vim keys, no command preview.
Shell: system tray icon and a self-updater.
Stack: C# / .NET 10, Terminal.Gui 2.5, shells out to winget.exe, winget only (no other package managers).

## Architecture

```
src/Wingman.Core   models, IWingetClient + WingetCliClient, output parsers, bundle (de)serialization,
                   settings, package-options store, history store. No UI dependency. Cross-platform.
src/Wingman.Tui    Terminal.Gui views. Depends on Core only.
src/Wingman        the `wingman` executable. No args: launches the TUI. Subcommands: headless CLI.
src/Wingman.Windows (phase 3) elevated helper, Task Scheduler registration, toasts, tray icon.
tests/Wingman.Core.Tests   xunit; parsers are tested against captured winget output in Fixtures/.
```

Dependency direction is strictly Core ← Tui ← Wingman. Windows-only code never lands in Core.

### Talking to winget

`WingetCliClient` runs `winget.exe` through `IProcessRunner` and parses stdout. Every call passes `--disable-interactivity --accept-source-agreements`. Reads (`list`, `search`, `upgrade`, `show`, `pin list`) are parsed by `WingetTableParser` (fixed-width tables keyed on header column offsets) and a key/value parser for `show`. Writes (`install`, `upgrade`, `uninstall`, `pin add/remove`) stream output line by line into the operation log and report the exit code.

Fixtures are captured on a real Windows machine with `tools/Capture-WingetFixtures.ps1` and committed under `tests/Wingman.Core.Tests/Fixtures/`. Parser changes must keep those tests green.

### Per-package options and UniGetUI compatibility

Wingman stores per-package install options using UniGetUI's `InstallOptions` schema verbatim, keyed by winget package id, in `package-options.json`. Because the on-disk model is the same one UniGetUI writes into bundles, exporting a bundle is a projection of the installed list plus that store, and importing one is a merge into it. Bundles are written with `export_version: 3` and `ManagerName: "WinGet"`. Packages from other managers in an imported bundle are listed as incompatible and skipped.

Ignore/hold maps to `winget pin add --blocking`; ignoring a single version is stored in `UpdatesOptions.IgnoredVersion` and filtered in the Updates view.

### State on disk

`%APPDATA%\Wingman\` (Environment.SpecialFolder.ApplicationData on every OS):
- `settings.json`: defaults (scope, source, flags, theme), schedule settings.
- `package-options.json`: `Dictionary<string, InstallOptions>` plus `Dictionary<string, UpdatesOptions>`.
- `history/<timestamp>-<op>-<id>.json` and `.log`: one pair per operation, pruned to the last 1000.

### Batch queue and elevation

The TUI marks packages, then builds an ordered queue of operations. Operations that need elevation are sent to a single elevated helper (`wingman --elevated-worker <pipe-name>`) started once per batch with ShellExecute `runas`, communicating over a named pipe. Non-elevated operations run in-process. Progress and log lines flow back into the history store and the TUI log pane.

## Phases

1. Core client and parsers with fixtures; TUI with the three views, details pane, filter, sort, install/upgrade/uninstall/pin for one package at a time, output log pane, background operations.
2. Batch queue, elevated helper, per-package options editor, pre/post commands, bundle import/export, history tab, settings tab, themes.
3. Headless CLI (`check`, `list`, `upgrade`, `export`, `import`, `history`), Task Scheduler registration, toasts, tray icon, self-updater.

## Testing strategy

Core is fully testable on macOS: parsers against fixtures, bundle round-trips, options store, queue ordering. The TUI is exercised manually on Windows. Every PR must keep `dotnet test` green on both OSes.
