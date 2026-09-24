using System.Net;
using System.Security.Cryptography;
using System.Text;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;

namespace TuiHarness;

/// <summary>
/// Stands in for the Windows setup executor: records each plan it is given and reports every item
/// created, or removed when asked to remove, without touching Task Scheduler or the registry.
/// </summary>
internal sealed class RecordingSetupExecutor : ISetupExecutor
{
    public List<(SetupPlan Plan, bool Remove)> Calls { get; } = [];

    public Task<IReadOnlyList<SetupResult>> ApplyAsync(SetupPlan plan, bool remove, bool dryRun, CancellationToken ct)
    {
        Calls.Add((plan, remove));
        var outcome = remove ? SetupResult.Removed : SetupResult.Created;
        var results = new List<SetupResult>();
        foreach (var item in SetupPlanner.Describe(plan))
        {
            results.Add(new SetupResult(item, outcome, null));
        }

        return Task.FromResult<IReadOnlyList<SetupResult>>(results);
    }
}

/// <summary>Stands in for running the Wingman installer: records each setup path and starts nothing.</summary>
internal sealed class RecordingSelfUpdateStarter : ISelfUpdateStarter
{
    public List<string> SetupPaths { get; } = [];

    public void StartInstaller(string setupPath) => SetupPaths.Add(setupPath);
}

/// <summary>
/// Stands in for GitHub: answers the latest-release request with <see cref="PublishedVersion"/> and
/// its win-x64 installer, and serves that installer's bytes and matching <c>.sha256</c>, so the
/// real <see cref="GitHubReleaseSource"/> and <see cref="UpdateDownloader"/> run without a network.
/// </summary>
internal sealed class FakeGitHubHandler : HttpMessageHandler
{
    public const string PublishedVersion = "9.9.9";
    public const string SetupName = $"wingman-v{PublishedVersion}-win-x64-setup.exe";
    private const string DownloadRoot = $"https://github.com/BenBurge/Wingman/releases/download/v{PublishedVersion}/";

    public static readonly byte[] SetupBytes = Encoding.ASCII.GetBytes("not a real installer");

    public int ReleaseRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        HttpContent? content = null;
        if (url == "https://api.github.com/repos/BenBurge/Wingman/releases/latest")
        {
            ReleaseRequests++;
            content = new StringContent(ReleaseJson, Encoding.UTF8, "application/json");
        }
        else if (url == DownloadRoot + SetupName)
        {
            content = new ByteArrayContent(SetupBytes);
        }
        else if (url == DownloadRoot + SetupName + ".sha256")
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(SetupBytes));
            content = new StringContent($"{hash}  {SetupName}\n");
        }

        var response = content is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        return Task.FromResult(response);
    }

    private static string ReleaseJson => $$"""
        {
          "tag_name": "v{{PublishedVersion}}",
          "html_url": "https://github.com/BenBurge/Wingman/releases/tag/v{{PublishedVersion}}",
          "assets": [
            { "name": "{{SetupName}}", "browser_download_url": "{{DownloadRoot}}{{SetupName}}" },
            { "name": "{{SetupName}}.sha256", "browser_download_url": "{{DownloadRoot}}{{SetupName}}.sha256" }
          ]
        }
        """;
}
