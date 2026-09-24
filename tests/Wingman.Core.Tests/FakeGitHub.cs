using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Wingman.Core.Tests;

/// <summary>
/// Answers the GitHub requests self-update makes from canned data instead of the network: the
/// latest-release JSON, installer bytes, and <c>.sha256</c> files, keyed by URL. Anything else is a
/// 404. Records every URL requested so a test can prove a cached answer made no request.
/// </summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string LatestUrl = "https://api.github.com/repos/BenBurge/Wingman/releases/latest";

    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public int LatestRequests => Requests.Count(request => request.RequestUri!.AbsoluteUri == LatestUrl);

    /// <summary>Thrown for every request instead of answering, as when the network is down.</summary>
    public Exception? Failure { get; set; }

    public static string DownloadUrl(string version, string fileName) =>
        $"https://github.com/BenBurge/Wingman/releases/download/v{version}/{fileName}";

    public static string SetupName(string version, string rid) => $"wingman-v{version}-{rid}-setup.exe";

    public static byte[] SetupBytes(string version) => Encoding.ASCII.GetBytes($"installer for {version}");

    /// <summary>A release carrying both runtimes' installers and hashes, as the release workflow publishes it.</summary>
    public static string ReleaseJson(string tag, params string[] assetNames)
    {
        var version = tag.TrimStart('v');
        var assets = new List<string>();
        foreach (var name in assetNames)
        {
            assets.Add($$"""{ "name": "{{name}}", "browser_download_url": "{{DownloadUrl(version, name)}}" }""");
        }

        return $$"""
            {
              "tag_name": "{{tag}}",
              "html_url": "https://github.com/BenBurge/Wingman/releases/tag/{{tag}}",
              "prerelease": false,
              "assets": [{{string.Join(",", assets)}}]
            }
            """;
    }

    /// <summary>Publishes <paramref name="version"/> with installers and matching hashes for both runtimes.</summary>
    public void Publish(string version, string? tag = null)
    {
        tag ??= "v" + version;
        var names = new List<string>();
        foreach (var rid in new[] { "win-x64", "win-arm64" })
        {
            var setup = SetupName(version, rid);
            names.Add(setup);
            names.Add(setup + ".sha256");
            var bytes = SetupBytes(version);
            Serve(DownloadUrl(version, setup), () => Bytes(bytes));
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Serve(DownloadUrl(version, setup + ".sha256"), () => Text($"{hash}  {setup}\n"));
        }

        ServeLatest(ReleaseJson(tag, [.. names]));
    }

    public void ServeLatest(string json) => Serve(LatestUrl, () => Text(json));

    public void ServeLatestStatus(HttpStatusCode status) => Serve(LatestUrl, () => new HttpResponseMessage(status));

    public void Serve(string url, Func<HttpResponseMessage> response) => _responses[url] = response;

    public static HttpResponseMessage Text(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) };

    public static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Failure is { } failure)
        {
            throw failure;
        }

        var response = _responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }
}
