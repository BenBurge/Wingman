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
        new SetupCommand(),
        new SelfUpdateCommand(),
        new TrayCommand(),
        new OpenCommand(),
    ];
}
