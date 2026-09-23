using Wingman.Core.Models;
using Wingman.Core.Updates;

namespace Wingman.Cli.Commands;

/// <summary><c>wingman list</c>: installed packages, optionally filtered, with held and excluded markers.</summary>
internal sealed class ListCommand : ICliCommand
{
    private const string HeldMarker = "⊘";
    private const string ExcludedMarker = "⟳";

    public string Name => "list";

    public string Summary => "List installed packages";

    public string Usage => """
        Usage: wingman list [query] [--json]

        Lists installed packages, only those whose name or id contains the query when one is given.
        ⊘ marks a package held by a blocking pin, ⟳ one excluded from updates.

          --json  Print the packages as one JSON document
        """;

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var query = args.Positionals.Count > 0 ? args.Positionals[0] : null;
        var installed = await context.Client.ListInstalledAsync(context.Cancel);
        var pins = await context.Client.ListPinsAsync(context.Cancel);

        var packages = new List<(PackageRow Row, UpdatePolicyKind Policy)>();
        foreach (var row in installed)
        {
            if (query is null || Matches(row, query))
            {
                var policy = UpdatePolicyResolver.Resolve(row, pins, context.Options.GetUpdatesOptions(row.Id));
                packages.Add((row, policy));
            }
        }

        if (context.Json)
        {
            WriteJson(context, packages);
            return ExitCodes.Success;
        }

        if (packages.Count == 0)
        {
            context.Out.WriteLine(query is null ? "No packages are installed." : $"No packages match '{query}'.");
            return ExitCodes.Success;
        }

        var table = new TableWriter("Name", "Id", "Version", "Available");
        foreach (var (row, policy) in packages)
        {
            table.AddMarkedRow(Marker(policy), row.Name, row.Id, row.Version, row.AvailableVersion ?? "");
        }

        table.Write(context.Out, context.Width);
        return ExitCodes.Success;
    }

    private static bool Matches(PackageRow row, string query) =>
        row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Marker(UpdatePolicyKind policy) => policy switch
    {
        UpdatePolicyKind.Hold => HeldMarker,
        UpdatePolicyKind.Exclude => ExcludedMarker,
        _ => "",
    };

    private static void WriteJson(CliContext context, List<(PackageRow Row, UpdatePolicyKind Policy)> packages)
    {
        var items = new List<object>(packages.Count);
        foreach (var (row, policy) in packages)
        {
            items.Add(new
            {
                name = row.Name,
                id = row.Id,
                version = row.Version,
                available = row.AvailableVersion,
                source = row.Source,
                held = policy == UpdatePolicyKind.Hold,
                excluded = policy == UpdatePolicyKind.Exclude,
            });
        }

        JsonOutput.Write(context.Out, new { packages = items });
    }
}
