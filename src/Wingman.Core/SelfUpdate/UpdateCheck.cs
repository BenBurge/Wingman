namespace Wingman.Core.SelfUpdate;

/// <summary>
/// The result of comparing the running build with the latest GitHub release.
/// <see cref="Latest"/> is null when GitHub could not be reached, or has no release with an
/// installer for this runtime, and nothing was cached from an earlier check.
/// </summary>
public sealed record UpdateCheck(string InstalledVersion, ReleaseInfo? Latest, bool IsNewerAvailable);
