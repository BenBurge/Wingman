# Wingman

Wingman is a keyboard-driven terminal UI (and headless CLI) for the Windows
Package Manager, `winget`, built in C# with Terminal.Gui.

**Status:** phase 3 is complete on the `phase-3-automation` branch. Wingman has
Installed, Discover, Updates, History, and Settings tabs; batch install, upgrade,
and uninstall with one UAC prompt per batch; per-package install options and
update policies; UniGetUI bundle export and import; four themes; a headless CLI
for scripting; and scheduled update checks and auto-install, toast notifications,
a system tray icon, and a self-updater, all registered by `wingman setup`.
`--fake` mode drives the whole UI (and every CLI command) from captured fixtures
on any OS.

## Install

Once the winget manifest is published:

```
winget install BenBurge.Wingman
```

Until then, download the release zip for your architecture, extract it
anywhere on your PATH, and run:

```
wingman setup
```

to register the scheduled update checks, the tray icon at login, and the
Start Menu shortcut toasts need.

## Command line

`wingman` with no arguments opens the terminal UI; each command below runs
headlessly instead. Every command takes `--fake` and, except where noted,
`--json`. See `docs/DESIGN.md` "Headless CLI" for the full option list.

| Command | Description | Exit codes |
|---|---|---|
| `check` | Check for updates | 0, 10 |
| `list [query]` | List installed packages | 0 |
| `search <query>` | Search winget for packages | 0, 2 |
| `upgrade` | Upgrade packages | 0, 1, 2, 130 |
| `install <id>...` | Install packages | 0, 1, 2, 130 |
| `export <file>` | Export installed packages to a bundle | 0, 2 |
| `import <file>` | Install the packages in a bundle | 0, 1, 2, 130 |
| `history` | Show past operations and their logs | 0, 2 |
| `setup` | Register scheduled checks, the tray icon, and shortcuts | 0, 1, 2 |
| `self-update` | Update Wingman through winget | 0, 1, 2, 10 |
| `tray` | Run the system tray icon | 0, 2 |
| `open [route]` | Open the terminal UI on a tab | 0, 1, 2 |

`0` success, `1` failure, `2` usage error, `10` updates available (`check`
only), `130` canceled.

## Build and test

```
dotnet build Wingman.sln
dotnet test Wingman.sln
```

### Local install

`pwsh tools/Install-Local.ps1` publishes a single-file build to
`%LOCALAPPDATA%\Programs\Wingman`, adds it to PATH, and registers the scheduled
tasks. `-Uninstall` reverses it; `-NoSetup` skips registration.

## Run

On Windows, against a real `winget`:

```
dotnet run --project src/Wingman
```

On any OS, driving the UI from captured fixtures instead:

```
dotnet run --project src/Wingman -- --fake
```

## Design

- `docs/DESIGN.md`: decisions, architecture, phases.
- `docs/mockups/wingman-screens.html`: every screen, the toasts, the tray icon, and the themes. Open it in a browser.
- Work is tracked in GitHub Issues, one issue per reviewable change, grouped by phase milestones.

## License

MIT. See [LICENSE](LICENSE).
