namespace Wingman.Core.Setup;

/// <summary>What <see cref="ISetupExecutor"/> did, or would do, with one part of a <see cref="SetupPlan"/>.</summary>
/// <param name="Outcome">One of the constants on this type.</param>
/// <param name="Error">The reason, when <paramref name="Outcome"/> is <see cref="Failed"/>; otherwise null.</param>
public sealed record SetupResult(SetupItem Item, string Outcome, string? Error)
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Removed = "removed";
    public const string Unchanged = "unchanged";
    public const string WouldCreate = "would create";
    public const string WouldRemove = "would remove";
    public const string Failed = "failed";
}
