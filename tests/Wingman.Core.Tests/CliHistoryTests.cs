using System.Text.Json;
using Wingman.Cli;
using Wingman.Core.Models;

namespace Wingman.Core.Tests;

public class CliHistoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public CliHistoryTests()
    {
        _cli.History.Append(
            Start,
            "upgrade",
            "AutoHotkey.AutoHotkey",
            "AutoHotkey",
            new OperationResult(0, true, TimeSpan.FromSeconds(2.1), ["Successfully upgraded"]),
            ["upgrade", "--id", "AutoHotkey.AutoHotkey", "--exact"],
            "batch-1");
        _cli.History.Append(
            Start.AddMinutes(1),
            "install",
            "Vendor.WillFail",
            "Vendor.WillFail",
            new OperationResult(1603, false, TimeSpan.FromSeconds(4), ["Starting package install...", "Installer failed with exit code: 1603"]),
            ["install", "--id", "Vendor.WillFail", "--exact"],
            "batch-1");
        _cli.History.Append(
            Start.AddMinutes(2),
            "batch",
            "batch-1",
            "2 operations",
            new OperationResult(1, false, TimeSpan.FromSeconds(6), ["✓ AutoHotkey.AutoHotkey upgrade 2.1 s"]),
            [],
            "batch-1");
    }

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task History_ListsOperationsNewestFirst_WithoutBatchEntries()
    {
        var exitCode = await _cli.RunAsync("history");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^# +When +Operation +Package +Result +Duration$", lines[0]);
        Assert.Matches(@"^1 +\d{4}-\d{2}-\d{2} \d{2}:\d{2} +install +Vendor\.WillFail +failed +4\.0 s$", lines[1]);
        Assert.Matches(@"^2 +\d{4}-\d{2}-\d{2} \d{2}:\d{2} +upgrade +AutoHotkey\.AutoHotkey +ok +2\.1 s$", lines[2]);
    }

    [Fact]
    public async Task HistoryFailed_ListsOnlyFailedOperations_KeepingTheirNumbers()
    {
        await _cli.RunAsync("history", "--failed");

        var lines = _cli.OutputLines;
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1 ", lines[1]);
        Assert.Contains("Vendor.WillFail", lines[1]);
    }

    [Fact]
    public async Task HistoryLast_LimitsTheCount()
    {
        await _cli.RunAsync("history", "--last", "1");

        Assert.Equal(2, _cli.OutputLines.Length);
    }

    [Fact]
    public async Task HistoryShow_PrintsTheHeaderAndTheFullLog()
    {
        var exitCode = await _cli.RunAsync("history", "show", "1");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal("install Vendor.WillFail (Vendor.WillFail)", lines[0]);
        Assert.Contains("Result:    failed, exit 1603", lines);
        Assert.Contains("Duration:  4.0 s", lines);
        Assert.Contains("Command:   winget install --id Vendor.WillFail --exact", lines);
        Assert.Contains("Batch:     batch-1", lines);
        Assert.Equal(["Starting package install...", "Installer failed with exit code: 1603"], lines[^2..]);
    }

    [Fact]
    public async Task HistoryForgetWithYes_DeletesTheEntry()
    {
        var exitCode = await _cli.RunAsync("history", "forget", "1", "--yes");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("Forgot install Vendor.WillFail from ", _cli.Output);
        Assert.DoesNotContain(_cli.History.List(), e => e.PackageId == "Vendor.WillFail");
        Assert.Contains(_cli.History.List(), e => e.PackageId == "AutoHotkey.AutoHotkey");
    }

    [Fact]
    public async Task HistoryForget_WithRedirectedStdinAndNoYes_KeepsTheEntry()
    {
        var exitCode = await _cli.RunAsync("history", "forget", "1");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains(_cli.History.List(), e => e.PackageId == "Vendor.WillFail");
    }

    [Theory]
    [InlineData("show", "3")]
    [InlineData("show", "0")]
    [InlineData("forget", "x")]
    public async Task History_NumberThatNamesNoEntry_IsAUsageError(string subcommand, string number)
    {
        var exitCode = await _cli.RunAsync("history", subcommand, number, "--yes");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.StartsWith($"wingman history: no entry {number}; the history has 2 entries", _cli.Error.ToString());
    }

    [Fact]
    public async Task HistoryJson_PrintsTheEntries()
    {
        await _cli.RunAsync("history", "--json");

        using var document = JsonDocument.Parse(_cli.Output);
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].GetProperty("number").GetInt32());
        Assert.Equal("Vendor.WillFail", entries[0].GetProperty("packageId").GetString());
        Assert.Equal("failed", entries[0].GetProperty("result").GetString());
        Assert.Equal(1603, entries[0].GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task History_AfterAnUpgrade_ShowsItsLog()
    {
        await _cli.RunAsync("upgrade", "GitHub.cli", "--yes");

        await _cli.RunAsync("history", "show", "1");

        Assert.StartsWith("upgrade GitHub.cli (GitHub CLI)", _cli.Output);
        Assert.Contains("Successfully upgraded", _cli.Output);
    }
}
