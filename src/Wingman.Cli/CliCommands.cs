namespace Wingman.Cli;

/// <summary>Every headless command, in the order <c>wingman --help</c> lists them.</summary>
public static class CliCommands
{
    public static IReadOnlyList<ICliCommand> All { get; } =
    [
        new NotImplementedCommand("check", "Check for updates; exits 10 when any are available"),
        new NotImplementedCommand("list", "List installed packages"),
        new NotImplementedCommand("search", "Search winget for packages"),
        new NotImplementedCommand("upgrade", "Upgrade packages"),
        new NotImplementedCommand("install", "Install packages"),
        new NotImplementedCommand("export", "Export installed packages to a UniGetUI bundle"),
        new NotImplementedCommand("import", "Install the packages in a UniGetUI bundle"),
        new NotImplementedCommand("history", "Show past operations and their logs"),
        new NotImplementedCommand("setup", "Register scheduled checks, the tray icon, and shortcuts"),
        new NotImplementedCommand("self-update", "Update Wingman through winget"),
        new NotImplementedCommand("tray", "Run the system tray icon"),
        new NotImplementedCommand("open", "Open the terminal UI on a tab"),
    ];
}
