using Wingman.Core.State;
using Wingman.Core.Winget;

namespace Wingman.Core.SelfUpdate;

/// <summary>
/// Checks whether a newer Wingman release is published on GitHub than the one currently running,
/// caching the answer in <c>state.json</c>.
/// </summary>
public static class SelfUpdateChecker
{
    /// <summary>The winget package id Wingman publishes itself under, which <c>upgrade --all</c> leaves to self-update.</summary>
    public const string PackageId = "BenBurge.Wingman";

    /// <summary>
    /// How long a cached answer is used before GitHub is asked again. GitHub allows 60 unauthenticated
    /// requests an hour per address, and every TUI start and scheduled check shares the cache.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private const string UnknownVersion = "0.0.0";

    /// <summary>
    /// Compares <paramref name="installedVersion"/> with the latest release. Uses the release cached
    /// in <paramref name="state"/> when it was read less than <paramref name="maxAge"/> before
    /// <paramref name="now"/>; otherwise asks <paramref name="source"/> and caches what it returns.
    /// When GitHub cannot answer, an older cached release still counts. Never throws except when
    /// <paramref name="ct"/> is canceled, so a failed check cannot block TUI startup.
    /// </summary>
    public static async Task<UpdateCheck> CheckAsync(
        GitHubReleaseSource source,
        string installedVersion,
        string rid,
        StateStore state,
        TimeSpan maxAge,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cached = state.Load();
        var cachedRelease = FromCache(cached, source);
        var age = now - cached.LastUpdateCheck;
        var isFresh = age is { } known && known >= TimeSpan.Zero && known < maxAge;

        ReleaseInfo? latest;
        if (isFresh)
        {
            latest = cachedRelease;
        }
        else
        {
            latest = await source.GetLatestAsync(rid, ct);
            if (latest is null)
            {
                latest = cachedRelease;
            }
            else
            {
                Store(state, latest, now);
            }
        }

        return new UpdateCheck(installedVersion, latest, IsNewer(installedVersion, latest));
    }

    /// <summary>Strips a leading <c>v</c>/<c>V</c> and any <c>-</c> or <c>+</c> suffix, so <c>v1.2.0-dev</c> compares as <c>1.2.0</c>.</summary>
    public static string NormalizeVersion(string version)
    {
        var trimmed = StripV(version.Trim());
        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        return suffixIndex < 0 ? trimmed : trimmed[..suffixIndex];
    }

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> name the same release once normalized.</summary>
    public static bool IsSameVersion(string left, string right) =>
        VersionComparer.Instance.Compare(NormalizeVersion(left), NormalizeVersion(right)) == 0;

    private static bool IsNewer(string installedVersion, ReleaseInfo? latest)
    {
        if (latest is null)
        {
            return false;
        }

        // A tag such as v1.3.0-beta.1 is a prerelease, which self-update never offers.
        if (StripV(latest.TagName.Trim()).Contains('-'))
        {
            return false;
        }

        // A dev build's version number carries no real ordering information, so it is never
        // reported as older than a published release.
        var installed = NormalizeVersion(installedVersion);
        if (installed == UnknownVersion)
        {
            return false;
        }

        return VersionComparer.Instance.Compare(installed, NormalizeVersion(latest.Version)) < 0;
    }

    private static ReleaseInfo? FromCache(WingmanState cached, GitHubReleaseSource source)
    {
        var hasRelease = cached.LatestVersion.Length > 0 && cached.LatestTag.Length > 0;
        if (!hasRelease
            || !Uri.TryCreate(cached.LatestSetupUrl, UriKind.Absolute, out var setupUrl)
            || !Uri.TryCreate(cached.LatestSha256Url, UriKind.Absolute, out var sha256Url))
        {
            return null;
        }

        return new ReleaseInfo(cached.LatestVersion, cached.LatestTag, setupUrl, sha256Url, source.ReleasePageUrl(cached.LatestTag));
    }

    // The cache only saves a request; a state.json another process holds open must not fail the check.
    private static void Store(StateStore state, ReleaseInfo latest, DateTimeOffset now)
    {
        try
        {
            state.Update(saved =>
            {
                saved.LastUpdateCheck = now;
                saved.LatestVersion = latest.Version;
                saved.LatestTag = latest.TagName;
                saved.LatestSetupUrl = latest.SetupUrl.AbsoluteUri;
                saved.LatestSha256Url = latest.Sha256Url.AbsoluteUri;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string StripV(string version) =>
        version.Length > 0 && version[0] is 'v' or 'V' ? version[1..] : version;
}
