# Wingman conventions

Build with `dotnet build`, test with `dotnet test`.

- `Wingman.Core` must stay free of Windows-only APIs and of any reference to
  Terminal.Gui. It is the cross-platform layer and must build and test on
  macOS and Linux.
- Never call `winget` from tests or at build time. `IWingetClient` and
  `IProcessRunner` exist so tests run against fixtures captured on Windows
  (see `tools/Capture-WingetFixtures.ps1` and
  `tests/Wingman.Core.Tests/Fixtures/`).
- Per-package install options mirror UniGetUI's `InstallOptions` /
  `UpdatesOptions` schema verbatim so bundles stay interchangeable with
  UniGetUI. Do not rename those properties.
- See `docs/DESIGN.md` for the full design and phase plan.
