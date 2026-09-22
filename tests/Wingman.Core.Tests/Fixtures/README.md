# Fixtures

These files are captured on a real Windows machine with `winget` installed,
using `tools/Capture-WingetFixtures.ps1` from the repo root, then copied here.
They are not hand-written and should not be edited by hand; if winget's
output format changes, recapture them.

Expected files:

- `list.txt` — `winget list`
- `upgrade.txt` — `winget upgrade`
- `upgrade-include-unknown.txt` — `winget upgrade --include-unknown`
- `search-vscode.txt` — `winget search "visual studio code"`
- `search-git.txt` — `winget search git`
- `search-nomatch.txt` — `winget search zzzznomatch`
- `show-vscode.txt` — `winget show Microsoft.VisualStudioCode`
- `show-versions-git.txt` — `winget show Git.Git --versions`
- `pin-list.txt` — `winget pin list`
- `version.txt` — `winget --version`
- `exit-codes.txt` — the exit code of each command above, `name=code` per line

Until these are captured, `WingetTableParserTests` uses inline sample output
instead of these fixtures.
