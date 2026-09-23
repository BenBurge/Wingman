using Wingman.Cli;
using Wingman.Core.Setup;

namespace Wingman.Core.Tests;

public class CliSetupTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Setup_WithoutAnExecutor_SaysWindowsOnlyAndReturnsUsage()
    {
        var exitCode = await _cli.RunAsync("setup");

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal($"wingman setup: Windows only{Environment.NewLine}", _cli.Error.ToString());
        Assert.Empty(_cli.Output);
    }

    [Fact]
    public async Task Setup_AppliesThePlanForTheExeAndSettings_AndPrintsOneLinePerPart()
    {
        var executor = new ScriptedSetupExecutor();
        _cli.SetupExecutor = executor;
        _cli.Settings.CheckIntervalHours = 12;

        var exitCode = await _cli.RunAsync("setup");

        Assert.Equal(ExitCodes.Success, exitCode);
        var call = Assert.Single(executor.Calls);
        Assert.False(call.Remove);
        Assert.False(call.DryRun);
        Assert.Equal(SetupPlanner.Build(_cli.Settings, _cli.ExePath).Tasks[0].Command, call.Plan.Tasks[0].Command);
        Assert.Contains("12", call.Plan.Tasks[0].SchtasksCreateArgs);

        var lines = _cli.OutputLines;
        Assert.Equal(SetupPlanner.Describe(call.Plan).Count, lines.Length);
        Assert.Equal(@"created      task     Wingman\Check", lines[0]);
        Assert.Equal("created      shortcut Wingman.lnk", lines[4]);
    }

    [Fact]
    public async Task Setup_DryRun_PassesDryRunAndPrintsWouldOutcomes()
    {
        var executor = new ScriptedSetupExecutor();
        _cli.SetupExecutor = executor;

        var exitCode = await _cli.RunAsync("setup", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(Assert.Single(executor.Calls).DryRun);
        Assert.Equal(@"would create task     Wingman\Check", _cli.OutputLines[0]);
        Assert.All(_cli.OutputLines, line => Assert.StartsWith("would create ", line));
    }

    [Fact]
    public async Task Setup_RemoveDryRun_PrintsWouldRemove()
    {
        var executor = new ScriptedSetupExecutor();
        _cli.SetupExecutor = executor;

        await _cli.RunAsync("setup", "--remove", "--dry-run");

        var call = Assert.Single(executor.Calls);
        Assert.True(call.Remove);
        Assert.True(call.DryRun);
        Assert.Equal("would remove registry Startup entry", _cli.OutputLines[3]);
    }

    [Fact]
    public async Task Setup_AFailure_PrintsTheErrorIndentedAndReturnsFailure()
    {
        _cli.SetupExecutor = new ScriptedSetupExecutor
        {
            FailName = @"Wingman\CheckAtLogon",
            FailError = "ERROR: Access is denied.",
        };

        var exitCode = await _cli.RunAsync("setup");

        Assert.Equal(ExitCodes.Failure, exitCode);
        var lines = _cli.OutputLines;
        Assert.Equal(@"created      task     Wingman\Check", lines[0]);
        Assert.Equal(@"failed       task     Wingman\CheckAtLogon", lines[1]);
        Assert.Equal("             ERROR: Access is denied.", lines[2]);
        Assert.Equal(@"created      task     Wingman\AutoInstall", lines[3]);
    }

    /// <summary>
    /// Records each call and answers every part with the outcome a clean machine would give, except
    /// <see cref="FailName"/>, which fails.
    /// </summary>
    private sealed class ScriptedSetupExecutor : ISetupExecutor
    {
        public List<(SetupPlan Plan, bool Remove, bool DryRun)> Calls { get; } = [];

        public string? FailName { get; init; }

        public string FailError { get; init; } = "";

        public Task<IReadOnlyList<SetupResult>> ApplyAsync(SetupPlan plan, bool remove, bool dryRun, CancellationToken ct)
        {
            Calls.Add((plan, remove, dryRun));
            var outcome = (remove, dryRun) switch
            {
                (true, true) => SetupResult.WouldRemove,
                (true, false) => SetupResult.Removed,
                (false, true) => SetupResult.WouldCreate,
                (false, false) => SetupResult.Created,
            };

            var results = new List<SetupResult>();
            foreach (var item in SetupPlanner.Describe(plan))
            {
                results.Add(item.Name == FailName
                    ? new SetupResult(item, SetupResult.Failed, FailError)
                    : new SetupResult(item, outcome, null));
            }

            return Task.FromResult<IReadOnlyList<SetupResult>>(results);
        }
    }
}
