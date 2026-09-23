using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;

namespace TuiHarness;

/// <summary>
/// Stands in for the Windows setup executor: records each plan it is given and reports every item
/// created, or removed when asked to remove, without touching Task Scheduler or the registry.
/// </summary>
internal sealed class RecordingSetupExecutor : ISetupExecutor
{
    public List<(SetupPlan Plan, bool Remove)> Calls { get; } = [];

    public Task<IReadOnlyList<SetupResult>> ApplyAsync(SetupPlan plan, bool remove, bool dryRun, CancellationToken ct)
    {
        Calls.Add((plan, remove));
        var outcome = remove ? SetupResult.Removed : SetupResult.Created;
        var results = new List<SetupResult>();
        foreach (var item in SetupPlanner.Describe(plan))
        {
            results.Add(new SetupResult(item, outcome, null));
        }

        return Task.FromResult<IReadOnlyList<SetupResult>>(results);
    }
}

/// <summary>Stands in for the detached winget upgrade of Wingman: counts the calls and starts nothing.</summary>
internal sealed class RecordingSelfUpdateStarter : ISelfUpdateStarter
{
    public int Calls { get; private set; }

    public void StartDetachedUpgrade() => Calls++;
}
