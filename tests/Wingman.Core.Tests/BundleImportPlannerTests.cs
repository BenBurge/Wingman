using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class BundleImportPlannerTests
{
    private static readonly IReadOnlyList<PackageRow> InstalledRows = WingetTableParser.Parse(Fixtures.Load("list.txt"));
    private static readonly IReadOnlyList<PackageRow> UpgradeRows = WingetTableParser.Parse(Fixtures.Load("upgrade.txt"));
    private static readonly Bundle FixtureBundle = BundleSerializer.Read(Fixtures.Load("bundle-unigetui.ubundle"));

    private static IReadOnlyList<ImportPlanRow> PlanFixtureBundle() =>
        BundleImportPlanner.Plan(FixtureBundle, InstalledRows, UpgradeRows);

    private static ImportPlanRow RowFor(IReadOnlyList<ImportPlanRow> plan, string id) =>
        Assert.Single(plan, row => row.Package.Id == id);

    [Fact]
    public void Plan_InstalledAtSameVersion_IsKeptAsAlreadyInstalled()
    {
        var row = RowFor(PlanFixtureBundle(), "Git.Git");

        Assert.Equal(ImportAction.Keep, row.Action);
        Assert.Equal("already installed", row.Reason);
        Assert.Equal("2.55.0.3", row.InstalledVersion);
    }

    [Fact]
    public void Plan_InstalledAtOlderVersionAndInUpgradeRows_IsUpgraded()
    {
        var row = RowFor(PlanFixtureBundle(), "AutoHotkey.AutoHotkey");

        Assert.Equal(ImportAction.Upgrade, row.Action);
        Assert.Equal("upgrade available", row.Reason);
        Assert.Equal("2.0.26", row.InstalledVersion);
    }

    [Fact]
    public void Plan_NotInstalled_IsInstalled()
    {
        var row = RowFor(PlanFixtureBundle(), "7zip.7zip");

        Assert.Equal(ImportAction.Install, row.Action);
        Assert.Equal("not installed", row.Reason);
        Assert.Equal("", row.InstalledVersion);
        Assert.Equal("machine", row.Package.InstallationOptions?.InstallationScope);
    }

    [Fact]
    public void Plan_BundleVersionLatestAndInstalled_IsKeptByTheLatestRule()
    {
        var row = RowFor(PlanFixtureBundle(), "Obsidian.Obsidian");

        Assert.Equal(ImportAction.Keep, row.Action);
        Assert.Equal("already installed", row.Reason);
        Assert.Equal("1.13.7", row.InstalledVersion);
    }

    [Fact]
    public void Plan_ManagerNameNotWinGet_IsIncompatibleWithManagerReason()
    {
        var row = RowFor(PlanFixtureBundle(), "ripgrep");

        Assert.Equal(ImportAction.Incompatible, row.Action);
        Assert.Equal("Scoop package", row.Reason);
    }

    [Fact]
    public void Plan_BundleIncompatiblePackagesEntry_IsIncompatibleWithNotFromWingetReason()
    {
        var row = RowFor(PlanFixtureBundle(), @"ARP\Machine\X64\Something");

        Assert.Equal(ImportAction.Incompatible, row.Action);
        Assert.Equal("not from winget", row.Reason);
    }

    [Fact]
    public void Plan_PreservesBundleOrderWithIncompatiblePackagesLast()
    {
        var plan = PlanFixtureBundle();

        var ids = plan.Select(row => row.Package.Id).ToArray();

        Assert.Equal(
            ["Git.Git", "AutoHotkey.AutoHotkey", "7zip.7zip", "Obsidian.Obsidian", "ripgrep", @"ARP\Machine\X64\Something"],
            ids);
    }

    [Fact]
    public void Summarize_FixtureBundle_CountsEachAction()
    {
        var summary = BundleImportPlanner.Summarize(PlanFixtureBundle());

        Assert.Equal(new ImportSummary(Install: 1, Upgrade: 1, Keep: 2, Skip: 0, Incompatible: 2), summary);
    }

    [Fact]
    public void Plan_BundleIdDiffersOnlyByCase_StillMatchesInstalledRow()
    {
        var bundle = new Bundle
        {
            Packages = [new BundlePackage { Id = "git.git", Name = "Git", Version = "2.55.0.3", Source = "winget", ManagerName = "WinGet" }],
        };

        var plan = BundleImportPlanner.Plan(bundle, InstalledRows, UpgradeRows);

        var row = Assert.Single(plan);
        Assert.Equal(ImportAction.Keep, row.Action);
        Assert.Equal("2.55.0.3", row.InstalledVersion);
    }

    [Fact]
    public void Plan_BundleVersionComparesNewerThanInstalled_IsUpgradedWithBundleVersionReason()
    {
        var bundle = new Bundle
        {
            Packages = [new BundlePackage { Id = "Notepad++.Notepad++", Name = "Notepad++", Version = "9.0.0", Source = "winget", ManagerName = "WinGet" }],
        };

        var plan = BundleImportPlanner.Plan(bundle, InstalledRows, UpgradeRows);

        var row = Assert.Single(plan);
        Assert.Equal(ImportAction.Upgrade, row.Action);
        Assert.Equal("bundle has 9.0.0", row.Reason);
        Assert.Equal("8.9.8", row.InstalledVersion);
    }

    [Fact]
    public void Plan_BundleVersionComparesOlderThanInstalled_IsKeptWithInstalledIsNewerReason()
    {
        var bundle = new Bundle
        {
            Packages = [new BundlePackage { Id = "Notepad++.Notepad++", Name = "Notepad++", Version = "8.9.0", Source = "winget", ManagerName = "WinGet" }],
        };

        var plan = BundleImportPlanner.Plan(bundle, InstalledRows, UpgradeRows);

        var row = Assert.Single(plan);
        Assert.Equal(ImportAction.Keep, row.Action);
        Assert.Equal("installed 8.9.8 is newer", row.Reason);
    }
}
