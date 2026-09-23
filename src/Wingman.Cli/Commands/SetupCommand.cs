using Wingman.Core.Setup;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman setup</c>: registers the scheduled tasks, the tray's startup entry, the Start Menu
/// shortcut, and the <c>wingman:</c> protocol from the current settings, or removes them.
/// </summary>
internal sealed class SetupCommand : ICliCommand
{
    private const int OutcomeWidth = 13;
    private const int KindWidth = 9;

    public string Name => "setup";

    public string Summary => "Register scheduled checks, the tray icon, and shortcuts";

    public string Usage => """
        Usage: wingman setup [--remove] [--dry-run]

        Registers the scheduled update check, the auto-install task, the tray's startup entry, the
        Start Menu shortcut toasts need, and the wingman: protocol, all for the current user. Parts
        whose setting is off are removed. Running it again changes only what differs. Windows only.

          --remove   Remove everything setup registers
          --dry-run  Print what would change and exit
        """;

    public IReadOnlyCollection<string> Flags => ["remove", "dry-run"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (context.SetupExecutor is not { } executor)
        {
            context.Error.WriteLine("wingman setup: Windows only");
            return ExitCodes.Usage;
        }

        var plan = SetupPlanner.Build(context.Settings, context.ExePath);
        var results = await executor.ApplyAsync(plan, args.HasFlag("remove"), args.HasFlag("dry-run"), context.Cancel);

        var failed = false;
        foreach (var result in results)
        {
            context.Out.WriteLine(FormatLine(result));
            if (result.Outcome == SetupResult.Failed)
            {
                failed = true;
                context.Out.WriteLine($"{new string(' ', OutcomeWidth)}{result.Error}");
            }
        }

        return failed ? ExitCodes.Failure : ExitCodes.Success;
    }

    /// <summary><c>created      task     Wingman\Check</c>: the outcome, the kind, and the name in columns.</summary>
    internal static string FormatLine(SetupResult result) =>
        $"{result.Outcome.PadRight(OutcomeWidth)}{result.Item.Kind.PadRight(KindWidth)}{result.Item.Name}";
}
