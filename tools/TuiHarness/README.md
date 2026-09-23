# TuiHarness

Runs the Wingman TUI headless on the ANSI driver at a fixed size against the fixture-backed fake, plays the scripted keys in `Program.cs`, and quits.
Run it with `dotnet run --project tools/TuiHarness -- 96 30 Midnight` (width, height, theme).
Every step appends a frame to `tools/TuiHarness/out.txt`, the screen dump: the text rows, and for some steps a map of one letter per cell with a legend of its colors.
It compiles the `src/Wingman.Tui` sources directly, so it sees internal types; it is not in `Wingman.sln`.
Steps can also check what their frame shows; each check appends a `check ok` or `check FAILED` line after the frame, and the harness exits with 1 when any check failed.
