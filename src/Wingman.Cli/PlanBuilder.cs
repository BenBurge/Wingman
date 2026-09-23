using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Settings;
using Wingman.Core.Updates;

namespace Wingman.Cli;

/// <summary>The operations a batch command would run, and one line for each package it left out.</summary>
internal sealed record Plan(IReadOnlyList<QueuedOperation> Operations, IReadOnlyList<string> Notes);

/// <summary>
/// Builds the operations for <c>upgrade</c>, <c>install</c>, and <c>import</c> the way the TUI queues
/// them: each package's stored <c>InstallOptions</c>, its update policy, and an elevation requirement
/// that also covers installers that usually need administrator rights.
/// </summary>
internal sealed class PlanBuilder
{
    private readonly CliContext _context;

    // One winget show per package per run, however many plans ask about it.
    private readonly Dictionary<string, PackageDetails?> _details = new(StringComparer.OrdinalIgnoreCase);

    public PlanBuilder(CliContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Upgrades for <paramref name="ids"/>, or for every available upgrade when <paramref name="all"/>.
    /// Held and excluded packages are left out with a note either way; <paramref name="all"/> also
    /// leaves out skipped versions, rows winget only upgrades when they are named, and Wingman
    /// itself, which only <c>wingman self-update</c> can replace.
    /// </summary>
    /// <param name="autoOnly">Limits <paramref name="all"/> to packages whose options turn on
    /// <c>AutoUpdatePackage</c>, for the scheduled auto-install.</param>
    public async Task<Plan> UpgradesAsync(IReadOnlyList<string> ids, bool all, bool autoOnly)
    {
        var ct = _context.Cancel;
        var upgrades = await _context.Client.ListUpgradesAsync(ct);
        var pins = await _context.Client.ListPinsAsync(ct);

        var operations = new List<QueuedOperation>();
        var notes = new List<string>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (all)
        {
            foreach (var row in upgrades)
            {
                var isAutoUpdated = _context.Options.GetInstallOptions(row.Id).AutoUpdatePackage;
                if ((autoOnly && !isAutoUpdated) || !planned.Add(row.Id))
                {
                    continue;
                }

                // winget cannot replace wingman.exe while this process runs it; self-update
                // upgrades it from a detached process after Wingman exits.
                if (string.Equals(row.Id, SelfUpdateChecker.PackageId, StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"skip {row.Id}: use wingman self-update");
                    continue;
                }

                if (row.RequiresExplicitTargeting)
                {
                    notes.Add($"skip {row.Id}: needs explicit targeting; name it to upgrade");
                    continue;
                }

                var policy = UpdatePolicyResolver.Resolve(row, pins, _context.Options.GetUpdatesOptions(row.Id));
                if (policy != UpdatePolicyKind.Update)
                {
                    notes.Add($"skip {row.Id}: {PolicyReason(policy, row)}");
                    continue;
                }

                operations.Add(await UpgradeAsync(row));
            }

            return new Plan(operations, notes);
        }

        IReadOnlyList<PackageRow>? installed = null;
        foreach (var id in ids)
        {
            if (!planned.Add(id))
            {
                continue;
            }

            var row = Find(upgrades, id);
            if (row is null)
            {
                installed ??= await _context.Client.ListInstalledAsync(ct);
                var reason = Find(installed, id) is null ? "not installed" : "already up to date";
                notes.Add($"skip {id}: {reason}");
                continue;
            }

            var policy = UpdatePolicyResolver.Resolve(row, pins, _context.Options.GetUpdatesOptions(row.Id));
            if (policy is UpdatePolicyKind.Hold or UpdatePolicyKind.Exclude)
            {
                notes.Add($"skip {row.Id}: {PolicyReason(policy, row)}");
                continue;
            }

            operations.Add(await UpgradeAsync(row));
        }

        return new Plan(operations, notes);
    }

    /// <summary>
    /// Installs for <paramref name="ids"/>. With <paramref name="version"/>, an installed package
    /// moves to that version instead: an upgrade when it is newer, an install with <c>--force</c>
    /// (a downgrade) when it is older. Without one, installed packages are left out with a note.
    /// </summary>
    public async Task<Plan> InstallsAsync(IReadOnlyList<string> ids, string? version)
    {
        var installed = await _context.Client.ListInstalledAsync(_context.Cancel);
        var hasVersion = !string.IsNullOrEmpty(version);

        var operations = new List<QueuedOperation>();
        var notes = new List<string>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            if (!planned.Add(id))
            {
                continue;
            }

            var installedRow = Find(installed, id);
            var options = _context.Options.GetInstallOptions(id);

            if (installedRow is not null && !hasVersion)
            {
                notes.Add($"skip {installedRow.Id}: already installed; use upgrade");
                continue;
            }

            OperationPlan? plan;
            PackageRow row;
            if (installedRow is not null)
            {
                row = installedRow;
                plan = OperationRequestFactory.CreateForVersion(row, version!, _context.Settings, options, isInstalled: true);
            }
            else
            {
                row = await CatalogRowAsync(id);
                plan = hasVersion
                    ? OperationRequestFactory.CreateForVersion(row, version!, _context.Settings, options, isInstalled: false)
                    : OperationRequestFactory.Create(OperationKind.Install, row, _context.Settings, options);
            }

            if (plan is null)
            {
                notes.Add($"skip {row.Id}: {version} is already installed");
                continue;
            }

            operations.Add(await WithElevationAsync(row, plan));
        }

        return new Plan(operations, notes);
    }

    /// <summary>
    /// <paramref name="plan"/> as a queue entry whose <see cref="OperationPlan.RequiresElevation"/>
    /// also covers the installer type, resolved under <see cref="ElevationMode.Auto"/> as the TUI
    /// does; the batch runner applies the configured mode and this process's elevation itself.
    /// </summary>
    public async Task<QueuedOperation> WithElevationAsync(PackageRow row, OperationPlan plan)
    {
        // Only Auto reads the installer type, and only when the options do not already decide it,
        // so the other modes skip the winget show.
        var detailsMatter = !plan.RequiresElevation
            && _context.Settings.ElevationMode == ElevationMode.Auto
            && !_context.ProcessIsElevated;
        var details = detailsMatter ? await DetailsAsync(row.Id) : null;

        var requiresElevation = ElevationHeuristic.Resolve(plan, details, ElevationMode.Auto, processIsElevated: false);
        return new QueuedOperation(plan.Kind, row, plan with { RequiresElevation = requiresElevation });
    }

    /// <summary>
    /// One plan line, <c>upgrade Git.Git 2.51.0 → 2.52.0</c>, <c>install Git.Git 2.52.0</c>, or
    /// <c>downgrade Git.Git 2.52.0 → 2.51.0</c>; a version winget has not named is left out.
    /// </summary>
    public static string Describe(QueuedOperation operation)
    {
        var label = operation.Plan.Label;
        var id = operation.Row.Id;
        var (from, to) = Versions(operation);
        if (from.Length > 0 && to.Length > 0)
        {
            return $"{label} {id} {from} → {to}";
        }

        var version = to.Length > 0 ? to : from;
        return version.Length > 0 ? $"{label} {id} {version}" : $"{label} {id}";
    }

    /// <summary>
    /// The installed version the operation starts from (<c>""</c> for a fresh install) and the one
    /// it targets (<c>""</c> when winget picks it and has not named it).
    /// </summary>
    public static (string From, string To) Versions(QueuedOperation operation)
    {
        var plan = operation.Plan;
        var row = operation.Row;
        var to = plan.Request.Version ?? row.AvailableVersion ?? "";

        var isFreshInstall = plan.Kind == OperationKind.Install && plan.Label != OperationPlan.DowngradeLabel;
        var from = isFreshInstall ? "" : row.Version;
        return (from, to);
    }

    /// <summary>A row for a package that is not installed, named from its <c>winget show</c> when winget knows it.</summary>
    private async Task<PackageRow> CatalogRowAsync(string id)
    {
        var details = await DetailsAsync(id);
        if (details is null)
        {
            return new PackageRow(Name: id, Id: id, Version: "", AvailableVersion: null, Source: "winget");
        }

        return new PackageRow(
            Name: details.Name.Length > 0 ? details.Name : id,
            Id: details.Id.Length > 0 ? details.Id : id,
            Version: "",
            AvailableVersion: details.Version.Length > 0 ? details.Version : null,
            Source: "winget");
    }

    private async Task<PackageDetails?> DetailsAsync(string id)
    {
        if (_details.TryGetValue(id, out var cached))
        {
            return cached;
        }

        PackageDetails? details;
        try
        {
            details = await _context.Client.ShowAsync(id, _context.Cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed show only loses the installer-type hint; winget reports the real problem
            // when the operation runs.
            details = null;
        }

        _details[id] = details;
        return details;
    }

    private async Task<QueuedOperation> UpgradeAsync(PackageRow row)
    {
        var options = _context.Options.GetInstallOptions(row.Id);
        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, row, _context.Settings, options);
        return await WithElevationAsync(row, plan);
    }

    private static string PolicyReason(UpdatePolicyKind policy, PackageRow row) => policy switch
    {
        UpdatePolicyKind.Hold => "held by a blocking pin",
        UpdatePolicyKind.Exclude => "excluded from updates",
        UpdatePolicyKind.SkipVersion => $"version {row.AvailableVersion} is skipped",
        _ => "",
    };

    internal static PackageRow? Find(IReadOnlyList<PackageRow> rows, string id)
    {
        foreach (var row in rows)
        {
            if (string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }
}
