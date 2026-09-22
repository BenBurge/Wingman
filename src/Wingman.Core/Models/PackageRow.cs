namespace Wingman.Core.Models;

/// <summary>
/// One row from <c>winget list</c>, <c>winget search</c>, or <c>winget upgrade</c>.
/// </summary>
public sealed record PackageRow(string Name, string Id, string Version, string? AvailableVersion, string Source);
