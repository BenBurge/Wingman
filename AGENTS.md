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
