# TuiHarness

Runs the Wingman TUI headless on the ANSI driver at a fixed size against the fixture-backed fake, plays the scripted keys in `Program.cs`, and quits.
Run it with `dotnet run --project tools/TuiHarness -- 96 30 Midnight` (width, height, theme).
Every step appends a frame to `tools/TuiHarness/out.txt`, the screen dump: the text rows, and for some steps a map of one letter per cell with a legend of its colors.
It compiles the `src/Wingman.Tui` sources directly, so it sees internal types; it is not in `Wingman.sln`.
Steps can also check what their frame shows; each check appends a `check ok` or `check FAILED` line after the frame, and the harness exits with 1 when any check failed.
Mouse steps inject clicks, double-clicks, right-clicks, and wheel events at screen cells. The run swaps in a fake clipboard and a URL opener that only records, so `Copy id` and `Open homepage` never touch the real clipboard or start a browser.
Some checks compare the frame the main loop drew incrementally between steps with the full redraw `Dump` forces, which catches views that only look right when everything is drawn again. At 96x24 the driver was back at 120x30 between steps, which breaks those checks and the mouse coordinates; 96x30 and 120x40 keep their size.
