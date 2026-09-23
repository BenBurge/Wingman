using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.SelfUpdate;

/// <summary>
/// Checks whether a newer Wingman release is published to winget than the one currently running.
/// </summary>
public static class SelfUpdateChecker
{
    /// <summary>The winget package id Wingman publishes itself under.</summary>
    public const string PackageId = "BenBurge.Wingman";

    private const string UnknownVersion = "0.0.0";

    /// <summary>
    /// Looks up <see cref="PackageId"/> and compares it against <paramref name="installedVersion"/>.
    /// Never throws: a client failure or an unknown package both come back as "not newer" with a
    /// null <see cref="SelfUpdateCheck.AvailableVersion"/>, so a failed check cannot block TUI startup.
    /// </summary>
    public static async Task<SelfUpdateCheck> CheckAsync(IWingetClient client, string installedVersion, CancellationToken ct)
    {
        PackageDetails? details;
        try
        {
            details = await client.ShowAsync(PackageId, ct);
        }
        catch (Exception)
        {
            return new SelfUpdateCheck(installedVersion, null, IsNewerAvailable: false);
        }

        if (details is null)
        {
            return new SelfUpdateCheck(installedVersion, null, IsNewerAvailable: false);
        }

        var normalizedInstalled = NormalizeVersion(installedVersion);
        if (normalizedInstalled == UnknownVersion)
        {
            // A dev build's version number carries no real ordering information, so it is
            // never reported as newer than a published release, but a real update still shows.
            return new SelfUpdateCheck(installedVersion, details.Version, IsNewerAvailable: false);
        }

        var isNewerAvailable = VersionComparer.Instance.Compare(normalizedInstalled, details.Version) < 0;
        return new SelfUpdateCheck(installedVersion, details.Version, isNewerAvailable);
    }

    /// <summary>Strips a leading <c>v</c>/<c>V</c> and any <c>-</c> or <c>+</c> suffix, so <c>v1.2.0-dev</c> compares as <c>1.2.0</c>.</summary>
    public static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.Length > 0 && trimmed[0] is 'v' or 'V')
        {
            trimmed = trimmed[1..];
        }

        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        return suffixIndex < 0 ? trimmed : trimmed[..suffixIndex];
    }
}
