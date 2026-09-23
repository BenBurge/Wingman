namespace Wingman.Core.Setup;

/// <summary>
/// Registers or removes what a <see cref="SetupPlan"/> describes. The implementation is
/// Windows-only; Core only defines the shape so the CLI and the TUI can run setup through it.
/// </summary>
public interface ISetupExecutor
{
    /// <summary>
    /// Applies every part of <paramref name="plan"/>, or tears it down when
    /// <paramref name="remove"/> is true, and returns one result per
    /// <see cref="SetupPlanner.Describe(SetupPlan)"/> item in the same order. A part whose
    /// <c>Enabled</c> is false is removed if present even when <paramref name="remove"/> is false,
    /// since its setting was turned off. A failed part does not stop the others. With
    /// <paramref name="dryRun"/> nothing is written and the results say what would happen.
    /// </summary>
    Task<IReadOnlyList<SetupResult>> ApplyAsync(SetupPlan plan, bool remove, bool dryRun, CancellationToken ct);
}
