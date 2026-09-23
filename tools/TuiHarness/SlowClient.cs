using Wingman.Core.Models;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Winget;

namespace TuiHarness;

/// <summary>
/// <see cref="FakeWingetClient"/> with winget-like delays, plus rows the fixtures lack: a wide-character
/// package at the end of the installed list whose <c>show</c> fails, one upgrade that needs
/// explicit targeting, and a catalog package, <c>Vendor.WillFail</c>, whose install prints more lines than
/// the log pane holds and then fails. Its <c>show</c> of Wingman itself reports <see cref="PublishedWingmanVersion"/>, so the
/// startup self-update check finds a newer release.
/// </summary>
internal sealed class SlowClient(IWingetClient inner) : IWingetClient
{
    public const string WideId = "Wide.漢字漢字漢字漢字漢字";
    public const string FailingId = "Vendor.WillFail";
    public const string PublishedWingmanVersion = "9.9.9";
    private const string ExplicitTargetingId = "Microsoft.VisualStudio.2022.Professional";

    // More than a 40-row terminal's log pane shows, so the wheel has something to scroll.
    private const int FailingInstallExtraLines = 40;

    // FakeWingetClient fails any operation on an Id containing "fail".
    private static readonly PackageRow FailingRow = new("Will Fail Tool", FailingId, "1.0.0", null, "winget");

    public Task<string> GetVersionAsync(CancellationToken ct) => inner.GetVersionAsync(ct);

    public async Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        var rows = await inner.SearchAsync(query, ct);
        var matchesFailingRow = query.Length > 0
            && (FailingRow.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || FailingRow.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        return matchesFailingRow ? [.. rows, FailingRow] : rows;
    }

    public async Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct)
    {
        await Task.Delay(800, ct);
        var rows = (await inner.ListInstalledAsync(ct)).ToList();
        rows.Add(new PackageRow("漢字漢字漢字漢字漢字漢字漢字 Wide Name", WideId, "10.0.1", null, "winget"));
        return rows;
    }

    public async Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct)
    {
        await Task.Delay(500, ct);
        var rows = await inner.ListUpgradesAsync(ct);
        return [.. rows.Select(row => row.Id == ExplicitTargetingId ? row with { RequiresExplicitTargeting = true } : row)];
    }

    public async Task<PackageDetails?> ShowAsync(string id, CancellationToken ct)
    {
        await Task.Delay(200, ct);
        if (id == WideId)
        {
            throw new InvalidOperationException("simulated show failure");
        }

        if (id == SelfUpdateChecker.PackageId)
        {
            return new PackageDetails { Id = id, Name = "Wingman", Version = PublishedWingmanVersion };
        }

        var details = await inner.ShowAsync(id, ct);
        return id == "Git.Git" && details is not null ? WithReleaseNotes(details) : details;
    }

    /// <summary>Long enough to scroll in a 40-row terminal, with a URL wider than the pane.</summary>
    private static PackageDetails WithReleaseNotes(PackageDetails details) => new()
    {
        Id = details.Id,
        Name = details.Name,
        Version = details.Version,
        Publisher = "The Git Development Community",
        Homepage = "https://gitforwindows.org/",
        License = "GNU GPL v2",
        Description = details.Description,
        ReleaseNotes = string.Join('\n',
            "Git for Windows v2.55.0 adds sparse checkout improvements and fixes a credential helper regression on ARM64 hosts.",
            "",
            "New features: the bundled OpenSSH is now 10.2, Git LFS is now 3.8.0, and scalar learned to register repositories cloned before it was installed.",
            "",
            "Bug fixes: a crash when the index is locked by another process is gone; git clone no longer hangs over slow HTTP proxies; long paths work again when core.longpaths is set in the system config.",
            "",
            "Known issues: the credential manager may ask for a token twice after upgrading from 2.4x, and git gui can render blank on high-contrast themes until it is restarted.",
            "",
            "Deprecations: the bundled Perl will be removed in a future release, so scripts that rely on it should install Perl separately; the legacy rebase backend is gone.",
            "",
            "Full notes: https://github.com/git-for-windows/git/releases/tag/v2.55.0.windows.3"),
        AdditionalFields = new Dictionary<string, string> { ["Installer.Scope"] = "machine" },
    };

    public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) => inner.ListVersionsAsync(id, ct);

    public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => inner.ListPinsAsync(ct);

    public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct)
    {
        if (request.Id == FailingId)
        {
            for (var i = 1; i <= FailingInstallExtraLines; i++)
            {
                output.Report($"Extracting file {i} of {FailingInstallExtraLines}");
            }
        }

        return inner.InstallAsync(request, output, ct);
    }

    public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        inner.UpgradeAsync(request, output, ct);

    public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        inner.UninstallAsync(request, output, ct);

    public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
        inner.PinAsync(id, blocking, version, ct);

    public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => inner.UnpinAsync(id, ct);
}
