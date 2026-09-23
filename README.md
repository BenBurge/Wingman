# Wingman

Wingman is a keyboard-driven terminal UI (and headless CLI) for the Windows
Package Manager, `winget`, built in C# with Terminal.Gui.

**Status:** phase 2 is complete on the `phase-2-batches` branch. Wingman has
Installed, Discover, Updates, History, and Settings tabs; batch install, upgrade,
and uninstall with one UAC prompt per batch; per-package install options and
update policies; UniGetUI bundle export and import; and four themes. Phase 3
(headless CLI, scheduled checks, toasts, tray icon) has not started. `--fake` mode
drives the whole UI from captured fixtures on any OS.

## Build and test

```
dotnet build Wingman.sln
dotnet test Wingman.sln
```

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
