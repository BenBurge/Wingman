namespace Wingman.Cli.Commands;

/// <summary><c>wingman search</c>: winget's search results, with installed packages marked.</summary>
internal sealed class SearchCommand : ICliCommand
{
    private const string InstalledMarker = "✓";

    public string Name => "search";

    public string Summary => "Search winget for packages";

    public string Usage => """
        Usage: wingman search <query> [--json]

        Searches winget for packages. ✓ marks a package that is already installed.

          --json  Print the results as one JSON document
        """;

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (args.Positionals.Count == 0)
        {
            context.Error.WriteLine("wingman search: a query is required");
            context.Error.WriteLine();
            context.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var query = args.Positionals[0];
        var results = await context.Client.SearchAsync(query, context.Cancel);
        var installed = await context.Client.ListInstalledAsync(context.Cancel);

        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in installed)
        {
            installedIds.Add(row.Id);
        }

        if (context.Json)
        {
            var packages = new List<object>(results.Count);
            foreach (var row in results)
            {
                packages.Add(new
                {
                    name = row.Name,
                    id = row.Id,
                    version = row.Version,
                    source = row.Source,
                    installed = installedIds.Contains(row.Id),
                });
            }

            JsonOutput.Write(context.Out, new { packages });
            return ExitCodes.Success;
        }

        if (results.Count == 0)
        {
            context.Out.WriteLine($"No packages match '{query}'.");
            return ExitCodes.Success;
        }

        var table = new TableWriter("Name", "Id", "Version");
        foreach (var row in results)
        {
            var marker = installedIds.Contains(row.Id) ? InstalledMarker : "";
            table.AddMarkedRow(marker, row.Name, row.Id, row.Version);
        }

        table.Write(context.Out, context.Width);
        return ExitCodes.Success;
    }
}
