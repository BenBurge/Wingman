namespace Wingman.Core.SelfUpdate;

/// <summary>
/// The result of comparing the running build's version against winget's published version for
/// <see cref="SelfUpdate.PackageId"/>. <see cref="AvailableVersion"/> is null when winget has no
/// record of the package or the check itself failed.
/// </summary>
public sealed record SelfUpdateCheck(string InstalledVersion, string? AvailableVersion, bool IsNewerAvailable);
