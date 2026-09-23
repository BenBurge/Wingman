using System.Text.Json;
using Wingman.Cli;
using Wingman.Core.Bundles;

namespace Wingman.Core.Tests;

public class CliCheckTests : IDisposable
{
    private const int FixtureUpgradeCount = 17;

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Check_PrintsTheUpdatesTableAndFooter_AndExits10()
    {
        var exitCode = await _cli.RunAsync("check");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        var lines = _cli.OutputLines;
        Assert.Matches(@"^Name +Id +Version +Available$", lines[0]);
        Assert.Matches(@"^AutoHotkey +AutoHotkey\.AutoHotkey +2\.0\.26 +2\.0\.28$", lines[1]);
        Assert.Equal($"{FixtureUpgradeCount} updates available · 0 held · 0 excluded", lines[^1]);
        Assert.Empty(_cli.Error.ToString());
    }

    [Fact]
    public async Task Check_MarksHeldRowsWithoutCountingThem_AndLeavesExcludedOut()
    {
        await _cli.Client.PinAsync("GitHub.cli", blocking: true, version: null, CancellationToken.None);
        _cli.Options.SetUpdatesOptions("Microsoft.WSL", new UpdatesOptions { UpdatesIgnored = true });

        var exitCode = await _cli.RunAsync("check");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        var lines = _cli.OutputLines;
        Assert.Single(lines, line => line.StartsWith("⊘ ", StringComparison.Ordinal));
        Assert.StartsWith("⊘ GitHub CLI", lines.Single(line => line.Contains("GitHub.cli")));
        Assert.DoesNotContain("Microsoft.WSL", _cli.Output);
        Assert.Equal($"{FixtureUpgradeCount - 2} updates available · 1 held · 1 excluded", lines[^1]);
    }

    [Fact]
    public async Task Check_Json_PrintsRowsWithPolicyAndCounts()
    {
        await _cli.Client.PinAsync("GitHub.cli", blocking: true, version: null, CancellationToken.None);
        _cli.Options.SetUpdatesOptions("Microsoft.WSL", new UpdatesOptions { UpdatesIgnored = true });

        var exitCode = await _cli.RunAsync("check", "--json");

        Assert.Equal(ExitCodes.UpdatesAvailable, exitCode);
        using var document = JsonDocument.Parse(_cli.Output);
        var root = document.RootElement;
        var updates = root.GetProperty("updates").EnumerateArray().ToList();
        Assert.Equal(FixtureUpgradeCount - 1, updates.Count);
        var held = updates.Single(u => u.GetProperty("id").GetString() == "GitHub.cli");
        Assert.Equal("hold", held.GetProperty("policy").GetString());
        Assert.Equal("2.101.0", held.GetProperty("available").GetString());
        Assert.Equal(1, root.GetProperty("held").GetInt32());
        Assert.Equal(1, root.GetProperty("excluded").GetInt32());
        Assert.True(root.GetProperty("checkedAt").TryGetDateTimeOffset(out _));
    }

    [Fact]
    public async Task Check_WritesTheStateFile()
    {
        _cli.State.Update(state => state.LastError = "winget was not found");
        await _cli.Client.PinAsync("GitHub.cli", blocking: true, version: null, CancellationToken.None);
        var before = DateTimeOffset.Now;

        await _cli.RunAsync("check");

        var state = _cli.State.Load();
        Assert.NotNull(state.LastCheck);
        Assert.True(state.LastCheck >= before.AddSeconds(-1));
        Assert.Equal(FixtureUpgradeCount - 1, state.UpdatesAvailable);
        Assert.Equal(FixtureUpgradeCount - 1, state.UpdateIds.Count);
        Assert.Contains("AutoHotkey.AutoHotkey", state.UpdateIds);
        Assert.DoesNotContain("GitHub.cli", state.UpdateIds);
        Assert.Equal("", state.LastError);
    }

    [Fact]
    public async Task Check_WithNothingToUpdate_SaysSoAndExits0()
    {
        var upgrades = await _cli.Client.ListUpgradesAsync(CancellationToken.None);
        foreach (var row in upgrades)
        {
            _cli.Options.SetUpdatesOptions(row.Id, new UpdatesOptions { UpdatesIgnored = true });
        }

        var exitCode = await _cli.RunAsync("check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"Everything is up to date.{Environment.NewLine}", _cli.Output);
        Assert.Equal(0, _cli.State.Load().UpdatesAvailable);
    }

    [Fact]
    public async Task Check_Notify_SendsOneToast()
    {
        await _cli.RunAsync("check");
        Assert.Empty(_cli.Toasts);

        await _cli.RunAsync("check", "--notify");

        var toast = Assert.Single(_cli.Toasts);
        Assert.Equal($"{FixtureUpgradeCount} updates available", toast.Title);
    }

    [Fact]
    public async Task Check_Notify_SendsNothingWhenPausedOrTurnedOff()
    {
        _cli.Settings.NotificationsPaused = true;
        await _cli.RunAsync("check", "--notify");

        _cli.Settings.NotificationsPaused = false;
        _cli.Settings.ToastOnUpdates = false;
        await _cli.RunAsync("check", "--notify");

        Assert.Empty(_cli.Toasts);
    }

    [Fact]
    public async Task Check_ClearsRunning_WhenItSucceeds()
    {
        _cli.State.Update(state => state.Running = true);

        await _cli.RunAsync("check");

        Assert.False(_cli.State.Load().Running);
    }

    [Fact]
    public async Task Check_ClearsRunningAndRecordsTheError_WhenWingetFails()
    {
        _cli.ClientOverride = new ScriptedWingetClient(_cli.Client)
        {
            ListUpgradesError = new InvalidOperationException("winget was not found"),
        };
        _cli.State.Update(state => state.Running = true);

        var exitCode = await _cli.RunAsync("check", "--notify");

        Assert.Equal(ExitCodes.Failure, exitCode);
        var state = _cli.State.Load();
        Assert.False(state.Running);
        Assert.Equal("winget was not found", state.LastError);
        Assert.Empty(_cli.Toasts);
    }
}
