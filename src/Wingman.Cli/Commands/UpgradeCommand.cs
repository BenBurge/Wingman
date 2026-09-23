namespace Wingman.Cli.Commands;

/// <summary><c>wingman upgrade</c>: upgrades named packages, or every available update, as one batch.</summary>
internal sealed class UpgradeCommand : ICliCommand
{
    public string Name => "upgrade";

    public string Summary => "Upgrade packages";

    public string Usage => """
        Usage: wingman upgrade [--all | <id>...] [--yes] [--dry-run] [--json] [--notify]

        Upgrades the named packages, or with --all every available update except held, excluded,
        and skipped ones. Prints the plan and asks before running it.

          --all      Upgrade everything that has an update
          --yes      Run without asking; required when stdin is not a terminal
          --dry-run  Print the plan and exit
          --json     Print the plan and its results as one JSON document
          --notify   Show a toast when the batch finishes
        """;

    // --auto is left out of the usage: only the scheduled auto-install task passes it.
    public IReadOnlyCollection<string> Flags => ["all", "yes", "dry-run", "notify", "auto"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var all = args.HasFlag("all");
        var hasIds = args.Positionals.Count > 0;
        if (all == hasIds)
        {
            var problem = all ? "pass --all or package ids, not both" : "name the packages to upgrade, or pass --all";
            context.Error.WriteLine($"wingman upgrade: {problem}");
            context.Error.WriteLine();
            context.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var plan = await new PlanBuilder(context).UpgradesAsync(args.Positionals, all, args.HasFlag("auto"));
        return await CliBatch.RunPlanAsync(context, args, plan);
    }
}
