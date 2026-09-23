using System.Text.Json;
using Wingman.Core.Bundles;
using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman import</c>: plans a UniGetUI bundle against what is installed, stores the bundle's
/// options, and runs its installs and upgrades through the same batch path as <c>upgrade</c>.
/// </summary>
internal sealed class ImportCommand : ICliCommand
{
    public string Name => "import";

    public string Summary => "Install the packages in a UniGetUI bundle";

    public string Usage => """
        Usage: wingman import <file> [--yes] [--dry-run] [--no-options] [--json]

        Reads a UniGetUI bundle (.ubundle) and prints what it would do with each package: install,
        upgrade, keep, or skip. Then stores the bundle's install and update options and runs the
        installs and upgrades.

          --yes         Run without asking; required when stdin is not a terminal
          --dry-run     Print the plan and exit
          --no-options  Keep the stored options instead of the bundle's
          --json        Print the operations and their results as one JSON document
        """;

    public IReadOnlyCollection<string> Flags => ["yes", "dry-run", "no-options"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (args.Positionals.Count == 0)
        {
            context.Error.WriteLine("wingman import: name the bundle to read");
            context.Error.WriteLine();
            context.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(args.Positionals[0]));
        Bundle bundle;
        try
        {
            bundle = BundleSerializer.Read(await File.ReadAllTextAsync(path, context.Cancel));
        }
        catch (JsonException ex)
        {
            context.Error.WriteLine($"wingman import: {path} is not a bundle: {ex.Message}");
            return ExitCodes.Failure;
        }

        var ct = context.Cancel;
        var installed = await context.Client.ListInstalledAsync(ct);
        var upgrades = await context.Client.ListUpgradesAsync(ct);
        var importPlan = BundleImportPlanner.Plan(bundle, installed, upgrades);

        var applyOptions = !args.HasFlag("no-options");
        var builder = new PlanBuilder(context);
        var operationsByRow = new QueuedOperation?[importPlan.Count];
        var operations = new List<QueuedOperation>();
        for (var i = 0; i < importPlan.Count; i++)
        {
            var operation = await BuildOperationAsync(context, builder, importPlan[i], installed, applyOptions);
            if (operation is not null)
            {
                operationsByRow[i] = operation;
                operations.Add(operation);
            }
        }

        var packagesWithOptions = PackagesWithOptions(importPlan);
        WritePlan(context, importPlan, operationsByRow, packagesWithOptions.Count, applyOptions);

        var plan = new Plan(operations, []);
        var dryRun = args.HasFlag("dry-run");
        var optionsToApply = applyOptions ? packagesWithOptions.Count : 0;
        if (dryRun || (operations.Count == 0 && optionsToApply == 0))
        {
            if (!dryRun)
            {
                CliBatch.TextOut(context).WriteLine("Nothing to do.");
            }

            CliBatch.WriteJson(context, plan, run: null, dryRun);
            return ExitCodes.Success;
        }

        var refusal = CliBatch.Confirm(context, args);
        if (refusal is int exitCode)
        {
            return exitCode;
        }

        if (applyOptions)
        {
            ApplyOptions(context, packagesWithOptions);
        }

        if (operations.Count == 0)
        {
            CliBatch.TextOut(context).WriteLine($"Applied the options of {optionsToApply} packages.");
            CliBatch.WriteJson(context, plan, run: null, dryRun);
            return ExitCodes.Success;
        }

        var run = await CliBatch.RunAsync(context, operations);
        CliBatch.WriteJson(context, plan, run, dryRun);
        return CliBatch.ExitCode(context, run.Summary);
    }

    /// <summary>
    /// The operation an install or upgrade row runs, with the bundle's options when they are to be
    /// applied and the stored ones otherwise; null for the rows that run nothing.
    /// </summary>
    private static async Task<QueuedOperation?> BuildOperationAsync(
        CliContext context,
        PlanBuilder builder,
        ImportPlanRow planRow,
        IReadOnlyList<PackageRow> installed,
        bool applyOptions)
    {
        var package = planRow.Package;
        var options = applyOptions && package.InstallationOptions is { } bundled
            ? bundled
            : context.Options.GetInstallOptions(package.Id);

        if (planRow.Action == ImportAction.Install)
        {
            var row = new PackageRow(package.Name, package.Id, package.Version, null, "winget");
            var plan = OperationRequestFactory.Create(OperationKind.Install, row, context.Settings, options);
            return await builder.WithElevationAsync(row, plan);
        }

        if (planRow.Action == ImportAction.Upgrade)
        {
            var row = PlanBuilder.Find(installed, package.Id)
                ?? new PackageRow(package.Name, package.Id, planRow.InstalledVersion, null, "winget");
            var plan = OperationRequestFactory.Create(OperationKind.Upgrade, row, context.Settings, options);

            // Without an available version winget knows of, the upgrade targets the bundle's newer one.
            if (string.IsNullOrEmpty(row.AvailableVersion))
            {
                plan = plan with { Request = plan.Request with { Version = package.Version } };
            }

            return await builder.WithElevationAsync(row, plan);
        }

        return null;
    }

    /// <summary>The packages that would be installed, upgraded, or kept and carry options in the bundle.</summary>
    private static List<BundlePackage> PackagesWithOptions(IReadOnlyList<ImportPlanRow> importPlan)
    {
        var packages = new List<BundlePackage>();
        foreach (var planRow in importPlan)
        {
            var isSelectable = planRow.Action is ImportAction.Install or ImportAction.Upgrade or ImportAction.Keep;
            var package = planRow.Package;
            var hasOptions = package.InstallationOptions is not null || package.Updates is not null;
            if (isSelectable && hasOptions)
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    private static void ApplyOptions(CliContext context, List<BundlePackage> packages)
    {
        foreach (var package in packages)
        {
            if (package.InstallationOptions is { } install)
            {
                context.Options.SetInstallOptions(package.Id, install);
            }

            if (package.Updates is { } updates)
            {
                context.Options.SetUpdatesOptions(package.Id, updates);
            }
        }
    }

    /// <summary>
    /// One row per bundle package with its action (<c>⚡</c> when it goes through the elevated
    /// helper), then the counts and what happens to the bundle's options.
    /// </summary>
    private static void WritePlan(
        CliContext context,
        IReadOnlyList<ImportPlanRow> importPlan,
        QueuedOperation?[] operationsByRow,
        int packagesWithOptions,
        bool applyOptions)
    {
        var writer = CliBatch.TextOut(context);
        var table = new TableWriter("Action", "Id", "Version", "Reason");
        for (var i = 0; i < importPlan.Count; i++)
        {
            var planRow = importPlan[i];
            var action = planRow.Action.ToString().ToLowerInvariant();
            var operation = operationsByRow[i];
            var usesHelper = operation is not null
                && ElevationPolicy.UsesHelper(operation.Plan, context.Settings.ElevationMode, context.ProcessIsElevated);
            if (usesHelper)
            {
                action += " ⚡";
            }

            table.AddRow(action, planRow.Package.Id, planRow.Package.Version, planRow.Reason);
        }

        table.Write(writer, context.Width);
        writer.WriteLine();

        var summary = BundleImportPlanner.Summarize(importPlan);
        var counts = $"Plan: {summary.Install} install · {summary.Upgrade} upgrade · {summary.Keep} keep";
        if (summary.Skip > 0)
        {
            counts += $" · {summary.Skip} skip";
        }

        counts += $" · {summary.Incompatible} incompatible";
        writer.WriteLine(counts);

        if (packagesWithOptions > 0)
        {
            var noun = packagesWithOptions == 1 ? "package" : "packages";
            writer.WriteLine(applyOptions
                ? $"Options: the bundle's options for {packagesWithOptions} {noun} will be stored"
                : $"Options: the bundle's options for {packagesWithOptions} {noun} are ignored (--no-options)");
        }
    }
}
