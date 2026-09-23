namespace Wingman.Core.Models;

/// <summary>
/// One row from <c>winget list</c>, <c>winget search</c>, or <c>winget upgrade</c>.
/// </summary>
/// <param name="RequiresExplicitTargeting">
/// True when winget listed this row in the second, "require explicit targeting for upgrade"
/// table, meaning <c>winget upgrade --all</c> skips it and it can only be upgraded by naming
/// its id directly.
/// </param>
public sealed record PackageRow(
    string Name,
    string Id,
    string Version,
    string? AvailableVersion,
    string Source,
    bool RequiresExplicitTargeting = false);
