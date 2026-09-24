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
src/Wingman.Core    models, IWingetClient + WingetCliClient, output parsers, bundle (de)serialization,
                    settings, package-options store, history store, state store, the setup plan
                    builder, and the toast content builder. No UI dependency. Cross-platform.
src/Wingman.Cli     headless commands (`check`, `list`, `search`, `upgrade`, `install`, `export`,
                    `import`, `history`, `setup`, `self-update`, `tray`, `open`). Cross-platform;
                    references Core only. Reaches Windows-only services only through
                    interfaces Core defines (`ISetupExecutor`, `IToastSender`, `ISelfUpdateStarter`,
                    the elevation factory); the host wires the real implementations in from
                    Wingman.Windows.
src/Wingman.Tui     Terminal.Gui views. Depends on Core only.
src/Wingman         the `wingman` executable. No args: launches the TUI. Subcommands: headless CLI.
src/Wingman.Windows elevated helper launcher and worker, the setup executor, the toast sender, the
                    tray icon process, and the self-update starter. Windows-only; referenced by
                    src/Wingman only.
tests/Wingman.Core.Tests   xunit; parsers are tested against captured winget output in Fixtures/;
                    headless commands are tested through CliHarness.
```

Dependency direction is strictly Core ← {Tui, Cli} ← Wingman, and Windows is referenced only by
Wingman. Windows-only code never lands in Core, Tui, or Cli.

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

The elevation mode setting picks which operations use the helper: Auto sends those whose options or installer need administrator rights, Always sends every operation, and Never starts no helper and lets winget and each installer prompt on their own. Auto reads the installer type from `winget show` when a package is queued and treats msi, wix, burn, exe, inno, and nullsoft installers not scoped to the user as needing elevation, because winget elevating an installer mid-run can be intercepted by a UAC broker such as Admin By Request; a Wingman already running as administrator runs everything in-process and never launches the helper.

The pipe itself is created by `SecurePipeServer` with an ACL granting only the current user and Administrators access; the helper is started with `--parent <pid>` and both ends check the other's process id (`GetNamedPipeServerProcessId` / `GetNamedPipeClientProcessId`) before trusting the connection, and the client connects at `TokenImpersonationLevel.Identification`, so it can read but not act as the server's identity.

### Bundle export and import

Both are reached with the `b` key on the Installed, Discover, and Updates tabs, and from the Tools row on the Settings tab. Export pre-selects every package winget can reinstall and lists the rest under `incompatible_packages`. Import shows a plan first (install, upgrade, keep, skip per row, with packages from other managers shown but unselectable), then hands the selected operations to the batch runner.

### Screens

Every list tab uses one widget: a filterable, sortable table on the left and a details pane on the right, with the tab strip and key bar fixed. The Updates tab title carries the available count. Held packages stay visible, dimmed with a hold marker. Marking rows builds a visible queue in the details pane, showing which operations need elevation. The batch runner replaces the content area rather than opening a modal. Right-clicking a row (or pressing `m`) opens a context menu with the row's actions.

### Distribution and background pieces

There is no Windows service. Wingman ships as a per-user Inno Setup installer (`installer/wingman.iss`, one per architecture), which the winget manifest also points at, and as a portable zip of the same single-file executable for users who would rather place it themselves and run `wingman setup`. The installer never asks for admin rights and never offers a machine-wide install. On install it closes the tray and stops any `wingman.exe` running from the install folder, copies `wingman.exe` to `%LOCALAPPDATA%\Programs\Wingman`, appends that folder to the user PATH when it is missing, runs `wingman setup`, and starts the tray headless when setup registered it to start at login. An upgrade is the same run over the existing folder: the running copies are stopped so the exe can be replaced, `setup` rewrites only what changed, and the tray is restarted, silent installs included, so winget and self-update upgrades leave it running. The uninstaller, listed in Settings → Apps, closes Wingman, runs `wingman setup --remove`, removes the folder from the user PATH, and deletes the program files; settings and history in `%APPDATA%\Wingman` are kept.

`wingman setup` idempotently registers, from the current settings: two scheduled tasks that run `wingman check --notify`, registered from Task Scheduler XML rather than plain `schtasks` flags so the definition can carry an interval trigger of up to 168 hours, a logon trigger bound to the current user, and laptop-friendly settings (allowed to start on battery, catches up on a missed run) — `Wingman\Check` on the interval and `Wingman\CheckAtLogon` at login, kept as its own task so the `CheckAtLogin` setting can remove it independently — a `Wingman\AutoInstall` task that runs `wingman upgrade --all --yes --auto --notify` daily at the configured time, a `Run` registry entry that starts `wingman tray` at login, a Start Menu shortcut (`Wingman.lnk`) carrying the AppUserModelID `BenBurge.Wingman` that toasts need, and the `wingman:` protocol under `HKCU\Software\Classes\wingman`, whose `shell\open\command` runs `wingman open "%1"`. `wingman setup --remove` tears every part down. Independent of `--remove`, a part whose own setting is off (`CheckAtLogin`, `AutoInstall`, or the tray's login-start setting) is removed on the next `setup` even without `--remove`, since its setting says it should not be there; a part that already matches what is registered is reported unchanged rather than rewritten. The startup entry and the scheduled tasks run `wingman` through `conhost.exe --headless`, so the console executable leaves no window on the desktop.

Toasts are shown by a hidden `powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden` process that loads the WinRT toast APIs and shows the XML `ToastBuilder` builds, so Wingman.Windows never takes a Windows SDK dependency; the AppUserModelID is only valid once `wingman setup` has created the Start Menu shortcut carrying it. Every toast activates through the `wingman:` protocol: the updates-available toast offers "Update all" (`wingman:update-all`) and "Open" (`wingman:updates`), and the batch-finished toast offers "View log" (`wingman:history`). Each kind of toast carries its own tag (`wingman-updates`, `wingman-batch`), so a later toast of the same kind replaces the last one instead of piling up, giving at most one toast per check. A toast can never fail the command that sent it: a PowerShell failure is written to standard error and otherwise ignored.

`state.json` holds the scheduler's last-known status for the tray to read without running a check of its own: `lastCheck`, `updatesAvailable`, and `updateIds` are written by `wingman check`; `lastBatch`, `lastBatchResult`, and `lastBatchFailed` are written after any batch (`upgrade`, `install`, `import`) finishes; `running` is set for the duration of a check or a batch, and briefly by the tray itself when it starts a check from its menu; `lastError` is set when a check's call to winget fails.

The tray (`wingman tray`) is a message-only window (`HWND_MESSAGE`), so it never appears in the taskbar and never receives `TaskbarCreated`; it re-adds its icon when a modify fails, watches `state.json` and `settings.json` with a debounced `FileSystemWatcher`, and re-reads both on a 60-second timer as a fallback. Selecting the icon and each menu item start a new `wingman` process rather than acting in place: "Update all" and selecting the icon itself open `wingman open update-all` and `wingman open updates`; "Check now" starts `wingman check --notify`; "Pause notifications" toggles `settings.json`'s pause flag directly; "Settings" opens `wingman open settings`; "Quit" closes the window. The icon is the W of Wingman whose last stroke lifts into a wing tip, drawn as a single 2-unit stroke on a 16-unit grid so it inherits the taskbar foreground color like the built-in tray icons. A bottom-right badge shows state: amber dot for updates available, amber ring for working, red dot for a failed last run, gray glyph when notifications are paused. The tray icons are embedded in `Wingman.Windows` and loaded with `CreateIconFromResourceEx`, so the single-file exe needs no files beside it. Left-click opens Wingman on the Updates tab in a new terminal window; right-click shows a menu with update-all, open, check now, pause notifications, settings, and quit. The app icon used for toasts and the Start Menu shortcut is the same W on an amber (#F0B54A) tile with a dark (#171B26) glyph.

Self-update compares the running version against `BenBurge.Wingman` on winget and, when a newer one is published, closes the tray if it is running and starts a detached `cmd.exe`, launching `timeout.exe` and `conhost.exe` by their fully qualified System32 paths rather than by name. The cmd waits two seconds (so this process has time to exit and release the executable, since a running executable cannot overwrite itself), runs `winget upgrade --id BenBurge.Wingman --exact --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity`, and, if the tray had been running, restarts it headless through `start` so `cmd.exe` does not linger for as long as the tray runs. The check never throws: a failed lookup, or a development build's unversioned build, is reported as "no update available" rather than blocking startup. Settings → Tools and the `setup`/`self-update` CLI commands share the same `ISetupExecutor` and `ISelfUpdateStarter`.

## Headless CLI

`wingman <command>` runs one of the commands below instead of the TUI; with no command, `wingman` launches the TUI. Every command accepts `--fake` (run against `FakeWingetClient` instead of a real `winget.exe`, the same fake the TUI's own `--fake` mode uses) and `--json` (print exactly one JSON document to standard output instead of a table; for the batch commands, plan and progress text that would otherwise go to standard output goes to standard error instead, so standard output holds only the document). `WINGMAN_DATA_DIR` overrides `%APPDATA%\Wingman` for `settings.json`, `package-options.json`, `state.json`, and the `history` folder; the TUI honors the same variable, which is how `tools/TuiHarness` and the test suite keep off the real profile.

| Command | Options | Does |
|---|---|---|
| `check` | `--notify` | Lists available upgrades and records the result in `state.json`; `--notify` shows a toast when any are available. |
| `list [query]` | | Installed packages, filtered to those whose name or id contains `query`. |
| `search <query>` | | winget's search results, with already-installed packages marked. |
| `upgrade --all \| <id>...` | `--yes` `--dry-run` `--notify` | Upgrades the named packages, or with `--all` every available update except held, excluded, and skipped ones. |
| `install <id>...` | `--version <v>` `--yes` `--dry-run` | Installs packages with their stored options, or with `--version` moves an installed one to that version. |
| `export <file>` | `--no-options` | Writes every installed package to a UniGetUI bundle (`.ubundle`). |
| `import <file>` | `--yes` `--dry-run` `--no-options` | Plans a bundle against what is installed, stores its options, and runs its installs and upgrades. |
| `history [--last n] [--failed]` | `--yes` | Lists past operations; `history show <n>` prints one operation's details and log, `history forget <n>` deletes it. |
| `setup [--remove] [--dry-run]` | | Registers or removes the scheduled tasks, the startup entry, the Start Menu shortcut, and the `wingman:` protocol; prints one line per part with `created`, `updated`, `removed`, `unchanged`, `would create`, `would remove`, or `failed`. Exits 0, or 1 when a part failed, 2 off Windows. |
| `self-update [--check]` | | With `--check`, exits 10 when a newer Wingman is on winget, 0 otherwise; without it, starts the detached `winget upgrade` and exits 0, or 1 when Wingman is not installed through winget, 2 off Windows. |
| `tray` | | Runs the tray icon process until the user quits it from its menu. Exits 2 off Windows. |
| `open [route] [--attached]` | | Opens the terminal UI on a route (`updates`, `update-all`, `history`, `settings`, `installed`, `discover`) in the current console, or, when there is no console window, spawns a new terminal window running the same command with `--attached`. The `wingman:` protocol handler calls it with the full link, which is parsed down to the route. |

`upgrade`, `install`, and `import` print their plan and ask `Proceed? [y/N]` before running it, unless `--dry-run` (print the plan and stop) or `--yes` (skip the question) is given; without `--yes`, a redirected standard input cannot answer and the command refuses to run.

Exit codes, shared by every command:

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | An operation in a batch failed, or the command threw (reported as `wingman: <message>`). |
| 2 | Usage: an unknown command or subcommand, a missing or invalid argument, or a batch command refusing to run without `--yes` because standard input is not a terminal. |
| 10 | `check` only: at least one non-held update is available. |
| 130 | Ctrl+C during a command, or the interactive `Proceed? [y/N]` prompt answered anything but `y`/`yes`. |

## Phases

1. Core client and parsers with fixtures; TUI with the three views, details pane, filter, sort, install/upgrade/uninstall/pin for one package at a time, output log pane, background operations. Done.
2. Batch queue, elevated helper, per-package options editor, pre/post commands, bundle import/export, history tab, settings tab, themes, context menus. Done.
3. Headless CLI (`check`, `list`, `search`, `upgrade`, `install`, `export`, `import`, `history`, `setup`, `self-update`, `tray`, `open`), the setup plan builder and executor, toast building and sending, the tray icon process, and the self-updater. Done. Five specific behaviors are covered only by fakes so far and need a manual check on a real Windows machine: `SetupExecutor` registering the scheduled tasks and shortcut against the real `schtasks.exe` and registry (tested against a scripted `IProcessRunner`), a real toast through the hidden PowerShell process having its actions actually launch `wingman` (tested against `ToastBuilder`'s XML and argv), the tray menu (tested only through its message-handling logic, not a real notification area), the elevated helper's UAC prompt including a decline, on a machine with a UAC broker such as Admin By Request in the mix (tested end to end only over an in-process named pipe), and a real self-update through winget (tested against a scripted `IProcessRunner` and `IWingetClient`).

## Testing strategy

Core and Cli are fully testable on macOS and Linux: parsers against fixtures, bundle round-trips, options store, queue ordering, setup plan building, toast content, and every headless command's argument parsing, `--json` output, and exit code through `CliHarness` (`tests/Wingman.Core.Tests/CliHarness.cs`), which runs `CliRunner` against a `FakeWingetClient` and a temporary data directory. `tools/TuiHarness` compiles the Tui sources into a console app on the ANSI driver, scripts keys and mouse events at a fixed size, and dumps every screen cell to `out.txt` so a layout or behavior change can be checked without a real terminal; run it after any TUI change. Windows-only code — the elevated helper launcher, `SetupExecutor`'s registry and Task Scheduler calls, `ToastNotifier`'s PowerShell process, and the tray window — is exercised manually on Windows. Every PR must keep `dotnet test` green on both OSes.
