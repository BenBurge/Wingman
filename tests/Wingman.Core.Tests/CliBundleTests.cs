using System.Text.Json;
using System.Text.RegularExpressions;
using Wingman.Cli;
using Wingman.Core.Bundles;

namespace Wingman.Core.Tests;

public class CliBundleTests : IDisposable
{
    private static readonly string FixtureBundle = Path.Combine(Fixtures.DirectoryPath, "bundle-unigetui.ubundle");

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string ExportPath => Path.Combine(_cli.DataDirectory, "exports", "out.ubundle");

    [Fact]
    public async Task Export_WritesABundleThatRoundTripsThroughTheSerializer()
    {
        _cli.Options.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });
        _cli.Options.SetUpdatesOptions("GitHub.cli", new UpdatesOptions { UpdatesIgnored = true });

        var exitCode = await _cli.RunAsync("export", ExportPath);

        Assert.Equal(ExitCodes.Success, exitCode);
        var bundle = BundleSerializer.Read(File.ReadAllText(ExportPath));
        Assert.Equal(3, bundle.ExportVersion);
        Assert.NotEmpty(bundle.IncompatiblePackages);
        Assert.All(bundle.Packages, p => Assert.Equal("WinGet", p.ManagerName));
        Assert.True(bundle.Packages.Single(p => p.Id == "Git.Git").InstallationOptions!.SkipHashCheck);
        Assert.True(bundle.Packages.Single(p => p.Id == "GitHub.cli").Updates!.UpdatesIgnored);

        var expected = $"Exported {bundle.Packages.Count} packages to {ExportPath} "
            + $"({bundle.IncompatiblePackages.Count} skipped: not from winget){Environment.NewLine}";
        Assert.Equal(expected, _cli.Output);
    }

    [Fact]
    public async Task ExportNoOptions_LeavesTheOptionsOut()
    {
        _cli.Options.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });

        await _cli.RunAsync("export", ExportPath, "--no-options");

        var bundle = BundleSerializer.Read(File.ReadAllText(ExportPath));
        Assert.All(bundle.Packages, p => Assert.Null(p.InstallationOptions));
    }

    [Fact]
    public async Task ExportJson_PrintsTheFileAndCounts()
    {
        await _cli.RunAsync("export", ExportPath, "--json");

        using var document = JsonDocument.Parse(_cli.Output);
        var root = document.RootElement;
        Assert.Equal(ExportPath, root.GetProperty("file").GetString());
        Assert.True(root.GetProperty("exported").GetInt32() > 0);
        Assert.True(root.GetProperty("skipped").GetInt32() > 0);
    }

    [Fact]
    public async Task ImportDryRun_PrintsEachRowsActionAndRunsNothing()
    {
        var exitCode = await _cli.RunAsync("import", FixtureBundle, "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Matches(@"^Action +Id +Version +Reason$", lines[0]);
        AssertRow(lines, "keep", "Git.Git", "already installed");
        AssertRow(lines, "upgrade", "AutoHotkey.AutoHotkey", "upgrade available");
        AssertRow(lines, "install ⚡", "7zip.7zip", "not installed");
        AssertRow(lines, "keep", "Obsidian.Obsidian", "already installed");
        AssertRow(lines, "incompatible", "ripgrep", "Scoop package");
        AssertRow(lines, "incompatible", @"ARP\Machine\X64\Something", "not from winget");
        Assert.Contains("Plan: 1 install · 1 upgrade · 2 keep · 2 incompatible", lines);
        Assert.Contains("Options: the bundle's options for 1 package will be stored", lines);

        Assert.Empty(_cli.History.List());
        Assert.True(_cli.Options.GetInstallOptions("7zip.7zip").IsDefault());
    }

    [Fact]
    public async Task ImportWithYes_StoresTheOptionsAndRunsTheInstallsAndUpgrades()
    {
        var exitCode = await _cli.RunAsync("import", FixtureBundle, "--yes");

        Assert.Equal(ExitCodes.Success, exitCode);
        var options = _cli.Options.GetInstallOptions("7zip.7zip");
        Assert.True(options.SkipHashCheck);
        Assert.Equal("machine", options.InstallationScope);

        var operations = _cli.History.List()
            .Where(e => e.Operation != "batch")
            .Select(e => $"{e.Operation} {e.PackageId}")
            .Order()
            .ToList();
        Assert.Equal(["install 7zip.7zip", "upgrade AutoHotkey.AutoHotkey"], operations);
        Assert.Matches(@"^2 of 2 succeeded in \d+ s$", _cli.OutputLines[^1]);
    }

    [Fact]
    public async Task ImportNoOptions_KeepsTheStoredOptions()
    {
        await _cli.RunAsync("import", FixtureBundle, "--yes", "--no-options");

        Assert.True(_cli.Options.GetInstallOptions("7zip.7zip").IsDefault());
        Assert.Contains("Options: the bundle's options for 1 package are ignored (--no-options)", _cli.OutputLines);
    }

    [Fact]
    public async Task Import_WithRedirectedStdinAndNoYes_RefusesBeforeStoringOptions()
    {
        var exitCode = await _cli.RunAsync("import", FixtureBundle);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.True(_cli.Options.GetInstallOptions("7zip.7zip").IsDefault());
        Assert.Empty(_cli.History.List());
    }

    private static void AssertRow(string[] lines, string action, string id, string reason)
    {
        var pattern = $"^{Regex.Escape(action)} +{Regex.Escape(id)} +\\S* *{Regex.Escape(reason)}$";
        Assert.Single(lines, line => Regex.IsMatch(line, pattern));
    }
}
