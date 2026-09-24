using System.Text.Json;
using Wingman.Cli;
using Wingman.Core.Bundles;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class CliListSearchTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task List_WithQuery_KeepsRowsWhoseNameOrIdMatches()
    {
        var exitCode = await _cli.RunAsync("list", "git");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Matches(@"^Name +Id +Version +Available$", lines[0]);
        Assert.Contains(lines, line => line.Contains("Git.Git"));
        Assert.Contains(lines, line => line.Contains("GitHub.cli") && line.EndsWith("2.101.0", StringComparison.Ordinal));
        Assert.All(lines.Skip(1), line => Assert.Contains("git", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_MarksHeldAndExcludedPackages()
    {
        await _cli.Client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);
        _cli.Options.SetUpdatesOptions("GitHub.cli", new UpdatesOptions { UpdatesIgnored = true });

        await _cli.RunAsync("list", "git");

        var lines = _cli.OutputLines;
        Assert.StartsWith("  Name", lines[0]);
        Assert.StartsWith("⊘ Git ", lines.Single(line => line.Contains("Git.Git")));
        Assert.StartsWith("⟳ GitHub CLI", lines.Single(line => line.Contains("GitHub.cli")));
    }

    [Fact]
    public async Task List_WithNoMatch_SaysSo()
    {
        var exitCode = await _cli.RunAsync("list", "zzz-nothing");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"No packages match 'zzz-nothing'.{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task List_Json_PrintsPackagesWithTheirPolicy()
    {
        await _cli.Client.PinAsync("Git.Git", blocking: true, version: null, CancellationToken.None);

        await _cli.RunAsync("list", "git", "--json");

        using var document = JsonDocument.Parse(_cli.Output);
        var packages = document.RootElement.GetProperty("packages").EnumerateArray().ToList();
        var git = packages.Single(p => p.GetProperty("id").GetString() == "Git.Git");
        Assert.Equal("Git", git.GetProperty("name").GetString());
        Assert.Equal("2.55.0.3", git.GetProperty("version").GetString());
        Assert.True(git.GetProperty("held").GetBoolean());
        Assert.False(git.GetProperty("excluded").GetBoolean());
    }

    [Fact]
    public async Task List_WithNoMatch_MakesNoPinCalls()
    {
        var counting = new CountingWingetClient(_cli.Client);
        _cli.ClientOverride = counting;

        await _cli.RunAsync("list", "zzz-nothing");

        Assert.Equal(0, counting.PinsCalls);
    }

    [Fact]
    public async Task List_WithMatches_CallsListPinsOnce()
    {
        var counting = new CountingWingetClient(_cli.Client);
        _cli.ClientOverride = counting;

        await _cli.RunAsync("list", "git");

        Assert.Equal(1, counting.PinsCalls);
    }

    [Fact]
    public async Task List_WhenOutputIsATerminal_ShowsTheWaitNotice()
    {
        _cli.IsOutputRedirected = false;

        await _cli.RunAsync("list");

        Assert.Contains("checking winget…", _cli.Error.ToString());
    }

    [Fact]
    public async Task List_TruncatesCellsToFitTheWidth()
    {
        _cli.Width = 60;

        await _cli.RunAsync("list");

        var lines = _cli.OutputLines;
        Assert.All(lines, line => Assert.True(DisplayWidth.Of(line) <= 60, $"'{line}' is wider than 60"));
        Assert.Contains(lines, line => line.Contains('…'));
    }

    [Fact]
    public async Task Search_MarksInstalledPackages()
    {
        var exitCode = await _cli.RunAsync("search", "git");

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = _cli.OutputLines;
        Assert.Matches(@"^  Name +Id +Version$", lines[0]);
        Assert.StartsWith("✓ Git ", lines.Single(line => line.Contains(" Git.Git ")));
        Assert.StartsWith("  git-crypt", lines.Single(line => line.Contains("AGWA.git-crypt")));
    }

    [Fact]
    public async Task Search_WithNoMatch_SaysSo()
    {
        var exitCode = await _cli.RunAsync("search", "zzz-nothing");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal($"No packages match 'zzz-nothing'.{Environment.NewLine}", _cli.Output);
    }

    [Fact]
    public async Task Search_WithoutAQuery_IsAUsageError()
    {
        var exitCode = await _cli.RunAsync("search");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.StartsWith("wingman search: a query is required", _cli.Error.ToString());
    }

    [Fact]
    public async Task Search_WhenOutputIsATerminal_ShowsTheWaitNotice()
    {
        _cli.IsOutputRedirected = false;

        await _cli.RunAsync("search", "git");

        Assert.Contains("checking winget…", _cli.Error.ToString());
    }

    [Fact]
    public async Task Search_Json_FlagsInstalledPackages()
    {
        await _cli.RunAsync("search", "git", "--json");

        using var document = JsonDocument.Parse(_cli.Output);
        var packages = document.RootElement.GetProperty("packages").EnumerateArray().ToList();
        Assert.True(packages.Single(p => p.GetProperty("id").GetString() == "Git.Git").GetProperty("installed").GetBoolean());
        Assert.False(packages.Single(p => p.GetProperty("id").GetString() == "AGWA.git-crypt").GetProperty("installed").GetBoolean());
    }
}
