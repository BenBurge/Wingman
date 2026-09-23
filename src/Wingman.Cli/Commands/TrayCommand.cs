namespace Wingman.Cli.Commands;

/// <summary><c>wingman tray</c>: runs the notification-area icon until it is quit.</summary>
internal sealed class TrayCommand : ICliCommand
{
    public string Name => "tray";

    public string Summary => "Run the system tray icon";

    public string Usage => """
        Usage: wingman tray

        Shows the Wingman icon in the notification area, badged from the last check and batch, and
        keeps running until Quit is chosen from its menu. A second tray process exits at once.
        Windows only; setup registers it to start at login.
        """;

    public Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var exitCode = context.TrayRunner?.Invoke(context.ExePath, context.DataDirectory);
        if (exitCode is not int code)
        {
            context.Error.WriteLine("wingman tray: Windows only");
            return Task.FromResult(ExitCodes.Usage);
        }

        return Task.FromResult(code);
    }
}
