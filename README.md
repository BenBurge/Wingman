# Wingman

Wingman is a keyboard-driven terminal UI (and headless CLI) for the Windows
Package Manager, `winget`, built in C# with Terminal.Gui.

**Status:** the phase 1 prototype is in progress on the `phase-1-prototype`
branch. It has the Installed, Discover, and Updates tabs with install,
upgrade, uninstall, and pin for one package at a time, plus a `--fake` mode
that drives the UI from captured fixtures.

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
