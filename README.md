# Wingman

Wingman is a keyboard-driven terminal UI (and headless CLI) for the Windows
Package Manager, `winget`, built in C# with Terminal.Gui.

**Status: pre-alpha, nothing works yet.** The solution builds and the core
parsers are tested, but there is no working install/upgrade/uninstall flow
and the TUI is a placeholder shell.

See `docs/DESIGN.md` for the design and phase plan.

## Build and test

```
dotnet build Wingman.sln
dotnet test Wingman.sln
```

Wingman is developed on macOS; `winget` itself only exists on Windows, so all
`winget` interaction sits behind `IWingetClient`, tested against fixtures
captured on a real Windows machine (`tools/Capture-WingetFixtures.ps1`).

## Design

- `docs/DESIGN.md`: decisions, architecture, phases.
- `docs/mockups/wingman-screens.html`: every screen, the toasts, the tray icon, and the themes. Open it in a browser.
- Work is tracked in GitHub Issues, one issue per reviewable change, grouped by phase milestones.

## License

MIT. See [LICENSE](LICENSE).
