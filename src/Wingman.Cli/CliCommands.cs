using Wingman.Cli.Commands;

namespace Wingman.Cli;

/// <summary>Every headless command, in the order <c>wingman --help</c> lists them.</summary>
public static class CliCommands
{
    public static IReadOnlyList<ICliCommand> All { get; } =
    [
        new CheckCommand(),
        new ListCommand(),
        new SearchCommand(),
        new UpgradeCommand(),
        new InstallCommand(),
        new ExportCommand(),
        new ImportCommand(),
        new HistoryCommand(),
        new NotImplementedCommand("setup", "Register scheduled checks, the tray icon, and shortcuts"),
        new NotImplementedCommand("self-update", "Update Wingman through winget"),
        new NotImplementedCommand("tray", "Run the system tray icon"),
        new NotImplementedCommand("open", "Open the terminal UI on a tab"),
    ];
}
