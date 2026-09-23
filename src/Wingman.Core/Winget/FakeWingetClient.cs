using System.Diagnostics;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Implements <see cref="IWingetClient"/> from the committed fixtures instead of shelling out to
/// <c>winget.exe</c>, so the TUI can be run and demoed on macOS and in CI. Reads answer straight
/// from parsed fixtures; installs, upgrades, and uninstalls stream a few canned lines with a
/// configurable delay and mutate in-memory state, succeeding unless the package id contains
/// "fail".
/// </summary>
public sealed class FakeWingetClient : IWingetClient
{
    private const int NoMatchExitCode = -1978335212;
    private const int InstallerFailedExitCode = 1603;
    private const string FakeSource = "winget";

    private readonly TimeSpan _stepDelay;
    private readonly string _version;

    // Both built once at construction and never mutated afterward: the dictionary is for id
    // lookup, the list keeps the dedup order Search results must appear in (Dictionary's
    // enumeration order is not a guaranteed contract).
    private readonly IReadOnlyDictionary<string, PackageRow> _catalogById;
    private readonly IReadOnlyList<PackageRow> _catalog;

    // Guards every mutable collection below: the TUI drives installs/upgrades/uninstalls from
    // background tasks while reads can happen concurrently.
    private readonly Lock _lock = new();
    private readonly List<PackageRow> _installed;
    private readonly List<PackageRow> _upgrades;
    private readonly List<Pin> _pins = [];

    public FakeWingetClient(TimeSpan? stepDelay = null)
    {
        _stepDelay = stepDelay ?? TimeSpan.FromMilliseconds(250);
        _version = EmbeddedFixtures.Read("version.txt").Trim();

        var installedRows = WingetTableParser.Parse(EmbeddedFixtures.Read("list.txt"));
        _installed = [.. installedRows];
        _upgrades = [.. WingetTableParser.Parse(EmbeddedFixtures.Read("upgrade.txt"))];

        var searchGitRows = WingetTableParser.Parse(EmbeddedFixtures.Read("search-git.txt"));
        var searchVsCodeRows = WingetTableParser.Parse(EmbeddedFixtures.Read("search-vscode.txt"));

        var catalogById = new Dictionary<string, PackageRow>(StringComparer.OrdinalIgnoreCase);
        var catalog = new List<PackageRow>();
        foreach (var row in searchGitRows.Concat(searchVsCodeRows).Concat(installedRows))
        {
            if (catalogById.TryAdd(row.Id, row))
            {
                catalog.Add(row);
            }
        }

        _catalogById = catalogById;
        _catalog = catalog;
    }

    public Task<string> GetVersionAsync(CancellationToken ct) => Task.FromResult(_version);

    public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Task.FromResult<IReadOnlyList<PackageRow>>([]);
        }

        var matches = _catalog
            .Where(row => string.Equals(row.Source, FakeSource, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<PackageRow>>(matches);
    }

    public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PackageRow>>([.. _installed]);
        }
    }

    public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PackageRow>>([.. _upgrades]);
        }
    }

    public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct)
    {
        if (string.Equals(id, "Microsoft.VisualStudioCode", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<PackageDetails?>(WingetShowParser.Parse(EmbeddedFixtures.Read("show-vscode.txt")));
        }

        var row = FindKnownRow(id);
        return Task.FromResult(row is null ? null : SynthesizeDetails(row));
    }

    public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct)
    {
        if (string.Equals(id, "Git.Git", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(WingetVersionsParser.Parse(EmbeddedFixtures.Read("show-versions-git.txt")));
        }

        var row = FindKnownRow(id);
        IReadOnlyList<string> versions = row is null ? [] : [row.Version];
        return Task.FromResult(versions);
    }

    public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<Pin>>([.. _pins]);
        }
    }

    public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct)
    {
        lock (_lock)
        {
            var installedRow = FindInstalledLocked(id);
            var pin = new Pin(
                Id: id,
                Name: installedRow?.Name ?? id,
                Version: installedRow?.Version ?? "",
                Source: installedRow?.Source ?? "",
                PinType: blocking ? PinType.Blocking : PinType.Pinning,
                PinnedVersion: version ?? "");

            _pins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            _pins.Add(pin);
        }

        return Task.FromResult(new OperationResult(0, true, TimeSpan.Zero, [$"Pin added for {id}."]));
    }

    public Task<OperationResult> UnpinAsync(string id, CancellationToken ct)
    {
        lock (_lock)
        {
            _pins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(new OperationResult(0, true, TimeSpan.Zero, [$"Pin removed for {id}."]));
    }

    public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct)
    {
        PackageRow? installedRow;
        lock (_lock)
        {
            installedRow = FindInstalledLocked(request.Id);
        }

        var isDowngrade = request.Force
            && installedRow is not null
            && !string.IsNullOrEmpty(request.Version)
            && VersionComparer.Instance.Compare(request.Version, installedRow.Version) < 0;
        if (isDowngrade)
        {
            return DowngradeAsync(request, installedRow!, output, ct);
        }

        var catalogRow = FindKnownRow(request.Id);
        string name;
        string version;
        PackageRow rowToInstall;
        if (catalogRow is null)
        {
            name = request.Id;
            version = request.Version ?? "1.0.0";
            rowToInstall = new PackageRow(Name: request.Id, Id: request.Id, Version: version, AvailableVersion: null, Source: FakeSource);
        }
        else
        {
            name = catalogRow.Name;
            version = request.Version ?? catalogRow.Version;
            var source = string.IsNullOrEmpty(catalogRow.Source) ? FakeSource : catalogRow.Source;
            rowToInstall = catalogRow with { Version = version, AvailableVersion = null, Source = source };
        }

        void OnSuccess()
        {
            lock (_lock)
            {
                if (!_installed.Exists(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _installed.Add(rowToInstall);
                }
            }
        }

        return StreamOperationAsync("install", "install", "installed", request.Id, name, version, OnSuccess, output, ct);
    }

    public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct)
    {
        PackageRow? installedRow;
        lock (_lock)
        {
            installedRow = FindInstalledLocked(request.Id);
        }

        if (installedRow is null)
        {
            return NoMatchResultAsync(output, ct);
        }

        var targetVersion = string.IsNullOrEmpty(request.Version)
            ? installedRow.AvailableVersion ?? installedRow.Version
            : request.Version;

        // A pinned or explicitly older/newer version than the known available one is still an
        // upgrade from what was installed, but it hasn't caught up to that available version, so
        // the row stays in _upgrades (with its installed version updated) instead of clearing.
        var reachedAvailableVersion = installedRow.AvailableVersion is null
            || string.Equals(targetVersion, installedRow.AvailableVersion, StringComparison.OrdinalIgnoreCase);

        void OnSuccess()
        {
            lock (_lock)
            {
                var index = _installed.FindIndex(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _installed[index] = _installed[index] with
                    {
                        Version = targetVersion,
                        AvailableVersion = reachedAvailableVersion ? null : installedRow.AvailableVersion,
                    };
                }

                if (reachedAvailableVersion)
                {
                    _upgrades.RemoveAll(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var upgradeIndex = _upgrades.FindIndex(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                    if (upgradeIndex >= 0)
                    {
                        _upgrades[upgradeIndex] = _upgrades[upgradeIndex] with { Version = targetVersion };
                    }
                }
            }
        }

        return StreamOperationAsync("upgrade", "upgrade", "upgraded", request.Id, installedRow.Name, targetVersion, OnSuccess, output, ct);
    }

    public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct)
    {
        PackageRow? installedRow;
        lock (_lock)
        {
            installedRow = FindInstalledLocked(request.Id);
        }

        if (installedRow is null)
        {
            return NoMatchResultAsync(output, ct);
        }

        void OnSuccess()
        {
            lock (_lock)
            {
                _installed.RemoveAll(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                _upgrades.RemoveAll(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
            }
        }

        return StreamOperationAsync("uninstall", "uninstall", "uninstalled", request.Id, installedRow.Name, installedRow.Version, OnSuccess, output, ct);
    }

    /// <summary>
    /// What <c>winget install --force --version</c> does to an installed package with that older
    /// version: the installed row takes it, and the version it came from, or the newer one already
    /// known to be available, becomes an upgrade.
    /// </summary>
    private Task<OperationResult> DowngradeAsync(
        OperationRequest request, PackageRow installedRow, IProgress<string> output, CancellationToken ct)
    {
        var version = request.Version!;
        var available = installedRow.AvailableVersion ?? installedRow.Version;

        void OnSuccess()
        {
            lock (_lock)
            {
                var downgraded = installedRow with { Version = version, AvailableVersion = available };
                var index = _installed.FindIndex(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _installed[index] = downgraded;
                }

                var upgradeIndex = _upgrades.FindIndex(r => string.Equals(r.Id, request.Id, StringComparison.OrdinalIgnoreCase));
                if (upgradeIndex >= 0)
                {
                    _upgrades[upgradeIndex] = _upgrades[upgradeIndex] with { Version = version, AvailableVersion = available };
                }
                else
                {
                    _upgrades.Add(downgraded);
                }
            }
        }

        return StreamOperationAsync("install", "install", "installed", request.Id, installedRow.Name, version, OnSuccess, output, ct);
    }

    /// <summary>A package known from the catalog, falling back to the installed list for rows the catalog fixtures never captured.</summary>
    private PackageRow? FindKnownRow(string id)
    {
        if (_catalogById.TryGetValue(id, out var catalogRow))
        {
            return catalogRow;
        }

        lock (_lock)
        {
            return FindInstalledLocked(id);
        }
    }

    private PackageRow? FindInstalledLocked(string id) =>
        _installed.Find(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));

    private static PackageDetails SynthesizeDetails(PackageRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Version = row.Version,
        Publisher = row.Id.Split('.')[0],
        Homepage = $"https://example.invalid/{row.Id}",
        Description = "Fixture data; winget show output was not captured for this package.",
        License = "Unknown",
    };

    /// <summary>
    /// Winget fails an upgrade or uninstall of a package it has no record of installing without
    /// downloading anything, so this skips the streamed install/upgrade steps entirely.
    /// </summary>
    private async Task<OperationResult> NoMatchResultAsync(IProgress<string> output, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        await Task.Delay(_stepDelay, ct);
        const string line = "No installed package found matching input criteria.";
        output.Report(line);
        stopwatch.Stop();
        return new OperationResult(NoMatchExitCode, false, stopwatch.Elapsed, [line]);
    }

    private async Task<OperationResult> StreamOperationAsync(
        string command,
        string verb,
        string successVerb,
        string id,
        string name,
        string version,
        Action onSuccess,
        IProgress<string> output,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var log = new List<string>();

        async Task ReportAsync(string line)
        {
            await Task.Delay(_stepDelay, ct);
            log.Add(line);
            output.Report(line);
        }

        await ReportAsync($"$ winget {command} --id {id} --exact --disable-interactivity --accept-source-agreements");
        await ReportAsync($"Found {name} [{id}] Version {version}");
        await ReportAsync("This application is licensed to you by its owner.");
        await ReportAsync($"Downloading https://example.invalid/{id}/{version}.exe");
        await ReportAsync("  ██████░░░░░░░░░░░░░░  30%");
        await ReportAsync("  ████████████░░░░░░░░  60%");
        await ReportAsync("  ████████████████████  100%");
        await ReportAsync($"Starting package {verb}...");

        // The fake's one hook for exercising failure handling: nothing in the fixtures models a
        // real installer failure, so any id containing "fail" stands in for one.
        if (id.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            await ReportAsync($"Installer failed with exit code: {InstallerFailedExitCode}");
            stopwatch.Stop();
            return new OperationResult(InstallerFailedExitCode, false, stopwatch.Elapsed, log);
        }

        await ReportAsync($"Successfully {successVerb}");
        onSuccess();

        stopwatch.Stop();
        return new OperationResult(0, true, stopwatch.Elapsed, log);
    }
}
