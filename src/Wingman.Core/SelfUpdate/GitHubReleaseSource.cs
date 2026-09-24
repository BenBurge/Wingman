using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Wingman.Core.SelfUpdate;

/// <summary>
/// Reads the latest release of a GitHub repository and picks the installer published for one
/// runtime, as <c>.github/workflows/release.yml</c> names it.
/// </summary>
public sealed class GitHubReleaseSource(HttpClient http, string owner = "BenBurge", string repo = "Wingman")
{
    private const string DevelopmentVersion = "0.0.0-dev";

    /// <summary>
    /// <c>Wingman/&lt;version&gt;</c>, which GitHub's API requires of every request, with any
    /// <c>+commit</c> suffix cut off.
    /// </summary>
    internal static ProductInfoHeaderValue UserAgent { get; } = CreateUserAgent();

    /// <summary>
    /// The latest published release, or null when there is none, it lacks an installer and hash
    /// for <paramref name="rid"/>, GitHub refuses the request (404, or 403/429 when rate limited),
    /// or the network fails. Only <paramref name="ct"/> being canceled throws.
    /// </summary>
    /// <param name="rid"><c>win-x64</c> or <c>win-arm64</c>.</param>
    public async Task<ReleaseInfo?> GetLatestAsync(string rid, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(document.RootElement, rid);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // An OperationCanceledException that is not the caller's is HttpClient's own timeout.
            return null;
        }
    }

    /// <summary>The page GitHub shows for <paramref name="tagName"/>, for a release read back from the cache.</summary>
    public string ReleasePageUrl(string tagName) => $"https://github.com/{owner}/{repo}/releases/tag/{tagName}";

    private ReleaseInfo? Parse(JsonElement release, string rid)
    {
        if (release.ValueKind != JsonValueKind.Object || StringProperty(release, "tag_name") is not { Length: > 0 } tag)
        {
            return null;
        }

        var version = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        var setupName = $"wingman-v{version}-{rid}-setup.exe";
        var sha256Name = setupName + ".sha256";

        Uri? setupUrl = null;
        Uri? sha256Url = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = StringProperty(asset, "name");
                var downloadUrl = StringProperty(asset, "browser_download_url");
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                if (name == setupName)
                {
                    setupUrl = uri;
                }
                else if (name == sha256Name)
                {
                    sha256Url = uri;
                }
            }
        }

        if (setupUrl is null || sha256Url is null)
        {
            return null;
        }

        var pageUrl = StringProperty(release, "html_url") ?? ReleasePageUrl(tag);
        return new ReleaseInfo(version, tag, setupUrl, sha256Url, pageUrl);
    }

    private static string? StringProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static ProductInfoHeaderValue CreateUserAgent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubReleaseSource).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var buildMetadata = version.IndexOf('+');
        if (buildMetadata >= 0)
        {
            version = version[..buildMetadata];
        }

        // A hand-stamped version a header token cannot carry falls back rather than throwing.
        var isValid = version.Length > 0 && ProductInfoHeaderValue.TryParse($"Wingman/{version}", out _);
        return new ProductInfoHeaderValue("Wingman", isValid ? version : DevelopmentVersion);
    }
}
