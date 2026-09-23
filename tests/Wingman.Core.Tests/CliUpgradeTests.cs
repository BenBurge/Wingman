using System.Text.Json;
using Wingman.Cli;
using Wingman.Core.Bundles;

namespace Wingman.Core.Tests;

public class CliUpgradeTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task UpgradeAllDryRun_PrintsThePlanAndRunsNothing()
    {
        var exitCode = await _cli.RunAsync("upgrade", "--all", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal(17, lines.Length);
        Assert.Equal("upgrade AutoHotkey.AutoHotkey 2.0.26 → 2.0.28", lines[0]);
        Assert.Empty(_cli.History.List());
        Assert.Equal(17, (await _cli.Client.ListUpgradesAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task UpgradeWithYes_RunsTheBatchWritesHistoryAndExits0()
    {
        var exitCode = await _cli.RunAsync("upgrade", "--yes", "AutoHotkey.AutoHotkey");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal("upgrade AutoHotkey.AutoHotkey 2.0.26 → 2.0.28", lines[0]);
        Assert.Equal("▶ upgrade AutoHotkey.AutoHotkey 2.0.26 → 2.0.28", lines[1]);
        Assert.Contains("  Successfully upgraded", lines);
        Assert.Matches(@"^✓ AutoHotkey\.AutoHotkey done in \d+\.\d s$", lines[^2]);
        Assert.Matches(@"^1 of 1 succeeded in \d+ s$", lines[^1]);

        var entry = Assert.Single(_cli.History.List(), e => e.Operation == "upgrade");
        Assert.Equal("AutoHotkey.AutoHotkey", entry.PackageId);
        Assert.True(entry.Succeeded);

        var state = _cli.State.Load();
        Assert.False(state.Running);
        Assert.NotNull(state.LastBatch);
        Assert.Equal("1 of 1 succeeded", state.LastBatchResult);
        Assert.False(state.LastBatchFailed);
    }

    [Fact]
    public async Task Upgrade_DropsUpgradedPackagesFromTheLastCheck()
    {
        await _cli.RunAsync("check");

        await _cli.RunAsync("upgrade", "AutoHotkey.AutoHotkey", "--yes");

        var state = _cli.State.Load();
        Assert.Equal(16, state.UpdatesAvailable);
        Assert.DoesNotContain("AutoHotkey.AutoHotkey", state.UpdateIds);
    }

    [Fact]
    public async Task InstallThatFails_Exits1WithTheFailureLine()
    {
        var exitCode = await _cli.RunAsync("install", "Vendor.WillFail", "--yes");

        Assert.Equal(ExitCodes.Failure, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal("install Vendor.WillFail", lines[0]);
        Assert.Contains("✗ Vendor.WillFail failed with exit 1603: Fatal error during installation.", lines);
        Assert.Matches(@"^0 of 1 succeeded, 1 failed in \d+ s$", lines[^1]);
        Assert.True(_cli.State.Load().LastBatchFailed);
    }

    [Fact]
    public async Task Upgrade_SkipsExcludedAndHeldIdsWithANote()
    {
        _cli.Options.SetUpdatesOptions("Microsoft.WSL", new UpdatesOptions { UpdatesIgnored = true });
        await _cli.Client.PinAsync("GitHub.cli", blocking: true, version: null, CancellationToken.None);

        var exitCode = await _cli.RunAsync("upgrade", "Microsoft.WSL", "GitHub.cli", "AutoHotkey.AutoHotkey", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] expected =
        [
            "upgrade AutoHotkey.AutoHotkey 2.0.26 → 2.0.28",
            "skip Microsoft.WSL: excluded from updates",
            "skip GitHub.cli: held by a blocking pin",
        ];
        Assert.Equal(expected, _cli.OutputLines);
    }

    [Fact]
    public async Task UpgradeAll_LeavesOutExcludedPackages()
    {
        _cli.Options.SetUpdatesOptions("Microsoft.WSL", new UpdatesOptions { UpdatesIgnored = true });

        await _cli.RunAsync("upgrade", "--all", "--dry-run");

        var lines = _cli.OutputLines;
        Assert.Equal(17, lines.Length);
        Assert.Equal("skip Microsoft.WSL: excluded from updates", lines[^1]);
        Assert.DoesNotContain("upgrade Microsoft.WSL", _cli.Output);
    }

    [Fact]
    public async Task UpgradeAuto_KeepsOnlyPackagesWithAutoUpdateOn()
    {
        _cli.Options.SetInstallOptions("GitHub.cli", new InstallOptions { AutoUpdatePackage = true });

        await _cli.RunAsync("upgrade", "--all", "--auto", "--dry-run");

        Assert.Equal(["upgrade GitHub.cli 2.98.0 → 2.101.0"], _cli.OutputLines);
    }

    [Fact]
    public async Task Upgrade_MarksOperationsThatNeedElevation()
    {
        _cli.Options.SetInstallOptions("GitHub.cli", new InstallOptions { RunAsAdministrator = true });

        await _cli.RunAsync("upgrade", "GitHub.cli", "--dry-run");

        Assert.Equal(["upgrade GitHub.cli 2.98.0 → 2.101.0 ⚡"], _cli.OutputLines);
    }

    [Fact]
    public async Task Upgrade_WithRedirectedStdinAndNoYes_RefusesAndExits2()
    {
        var exitCode = await _cli.RunAsync("upgrade", "AutoHotkey.AutoHotkey");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal($"{CliBatch.RefuseText}{Environment.NewLine}", _cli.Error.ToString());
        Assert.Empty(_cli.History.List());
    }

    [Fact]
    public async Task Upgrade_AnsweredYesAtThePrompt_Runs()
    {
        _cli.IsInputRedirected = false;
        _cli.Input = "y\n";

        var exitCode = await _cli.RunAsync("upgrade", "AutoHotkey.AutoHotkey");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Proceed? [y/N] ", _cli.Output);
        Assert.NotEmpty(_cli.History.List());
    }

    [Fact]
    public async Task Upgrade_AnsweredNoAtThePrompt_RunsNothing()
    {
        _cli.IsInputRedirected = false;
        _cli.Input = "\n";

        var exitCode = await _cli.RunAsync("upgrade", "AutoHotkey.AutoHotkey");

        Assert.Equal(ExitCodes.Canceled, exitCode);
        Assert.EndsWith($"Proceed? [y/N] Canceled.{Environment.NewLine}", _cli.Output);
        Assert.Empty(_cli.History.List());
    }

    [Fact]
    public async Task Upgrade_Json_PrintsTheSummaryAndSendsProgressToStandardError()
    {
        var exitCode = await _cli.RunAsync("upgrade", "AutoHotkey.AutoHotkey", "--yes", "--json");

        Assert.Equal(ExitCodes.Success, exitCode);
        using var document = JsonDocument.Parse(_cli.Output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("dryRun").GetBoolean());
        var operation = Assert.Single(root.GetProperty("operations").EnumerateArray());
        Assert.Equal("upgrade", operation.GetProperty("operation").GetString());
        Assert.Equal("2.0.26", operation.GetProperty("from").GetString());
        Assert.Equal("2.0.28", operation.GetProperty("to").GetString());
        Assert.Equal("succeeded", operation.GetProperty("result").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("succeeded").GetInt32());
        Assert.Contains("▶ upgrade AutoHotkey.AutoHotkey", _cli.Error.ToString());
    }

    [Fact]
    public async Task Upgrade_WithNeitherIdsNorAll_IsAUsageError()
    {
        var exitCode = await _cli.RunAsync("upgrade");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.StartsWith("wingman upgrade: name the packages to upgrade, or pass --all", _cli.Error.ToString());
    }

    [Fact]
    public async Task Upgrade_NamedPackageWithNoUpdate_IsSkippedWithANote()
    {
        await _cli.RunAsync("upgrade", "Git.Git", "Vendor.Missing", "--yes");

        string[] expected = ["skip Git.Git: already up to date", "skip Vendor.Missing: not installed", "Nothing to do."];
        Assert.Equal(expected, _cli.OutputLines);
    }

    [Fact]
    public async Task InstallOlderVersionOfAnInstalledPackage_PlansADowngrade()
    {
        await _cli.RunAsync("install", "Git.Git", "--version", "2.40.0", "--dry-run");

        Assert.Equal(["downgrade Git.Git 2.55.0.3 → 2.40.0"], _cli.OutputLines);
    }

    [Fact]
    public async Task InstallOfAnInstalledPackageWithoutAVersion_IsSkippedWithANote()
    {
        await _cli.RunAsync("install", "Git.Git", "--dry-run");

        Assert.Equal(["skip Git.Git: already installed; use upgrade", "Nothing to do."], _cli.OutputLines);
    }

    [Fact]
    public async Task Upgrade_Notify_SendsOneBatchToast_UnlessToastOnBatchIsOff()
    {
        await _cli.RunAsync("upgrade", "--yes", "--notify", "AutoHotkey.AutoHotkey");

        var toast = Assert.Single(_cli.Toasts);
        Assert.Equal("1 of 1 updated", toast.Title);

        _cli.Settings.ToastOnBatch = false;
        await _cli.RunAsync("upgrade", "--yes", "--notify", "GitHub.cli");

        Assert.Single(_cli.Toasts);
    }
}
