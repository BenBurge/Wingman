namespace Wingman.Cli.Commands;

/// <summary><c>wingman install</c>: installs packages, or moves an installed one to a given version.</summary>
internal sealed class InstallCommand : ICliCommand
{
    public string Name => "install";

    public string Summary => "Install packages";

    public string Usage => """
        Usage: wingman install <id>... [--version <v>] [--yes] [--dry-run] [--json]

        Installs the named packages with their stored install options. With --version, an
        installed package is upgraded or downgraded to that version instead.

          --version <v>  Install this version
          --yes          Run without asking; required when stdin is not a terminal
          --dry-run      Print the plan and exit
          --json         Print the plan and its results as one JSON document
        """;

    public IReadOnlyCollection<string> Flags => ["yes", "dry-run"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var version = args.GetOption("version");
        string? problem = null;
        if (args.Positionals.Count == 0)
        {
            problem = "name the packages to install";
        }
        else if (version is { Length: 0 })
        {
            problem = "--version needs a value";
        }

        if (problem is not null)
        {
            context.Error.WriteLine($"wingman install: {problem}");
            context.Error.WriteLine();
            context.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var plan = await new PlanBuilder(context).InstallsAsync(args.Positionals, version);
        return await CliBatch.RunPlanAsync(context, args, plan);
    }
}
