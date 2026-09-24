using System.Globalization;
using System.Security.Cryptography;

namespace Wingman.Core.SelfUpdate;

/// <summary>
/// Downloads a release's installer and checks it against the SHA-256 published beside it, so a
/// truncated or tampered download is never run.
/// </summary>
public sealed class UpdateDownloader(HttpClient http)
{
    private const int Sha256HexLength = 64;

    /// <summary>Where the TUI and CLI download installers to: a <c>Wingman\updates</c> folder under the user's temp folder.</summary>
    public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "Wingman", "updates");

    /// <summary>
    /// Downloads <see cref="ReleaseInfo.SetupUrl"/> into <paramref name="directory"/> and returns the
    /// installer's path once its SHA-256 matches the published one. The file is written as
    /// <c>&lt;name&gt;.partial</c> and renamed only after it matches, so a file with the final name
    /// is always a verified one.
    /// </summary>
    /// <exception cref="InvalidDataException">The published hash is malformed, or the download does not match it.</exception>
    /// <exception cref="HttpRequestException">Either download failed.</exception>
    public async Task<string> DownloadVerifiedAsync(ReleaseInfo release, string directory, CancellationToken ct)
    {
        var expectedHash = await DownloadPublishedHashAsync(release.Sha256Url, ct);

        var fileName = Path.GetFileName(release.SetupUrl.LocalPath);
        if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"The installer URL does not name a file: {release.SetupUrl}");
        }

        Directory.CreateDirectory(directory);
        var setupPath = Path.Combine(directory, fileName);
        var partialPath = setupPath + ".partial";

        byte[] actualHash;
        try
        {
            actualHash = await DownloadHashingAsync(release.SetupUrl, partialPath, ct);
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }

        if (!string.Equals(Convert.ToHexString(actualHash), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidDataException("Downloaded installer does not match its published SHA-256");
        }

        File.Move(partialPath, setupPath, overwrite: true);
        return setupPath;
    }

    /// <summary>Reads the <c>"&lt;hash&gt;  &lt;file&gt;"</c> line the release workflow writes and returns its hash.</summary>
    private async Task<string> DownloadPublishedHashAsync(Uri sha256Url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sha256Url);
        request.Headers.UserAgent.Add(GitHubReleaseSource.UserAgent);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);

        var tokens = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hash = tokens.Length > 0 ? tokens[0].TrimStart('﻿') : "";
        var isHex = hash.Length == Sha256HexLength && hash.All(char.IsAsciiHexDigit);
        if (!isHex)
        {
            throw new InvalidDataException(string.Format(
                CultureInfo.InvariantCulture, "The published SHA-256 at {0} is not a {1}-character hex hash", sha256Url, Sha256HexLength));
        }

        return hash;
    }

    private async Task<byte[]> DownloadHashingAsync(Uri url, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(GitHubReleaseSource.UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return hash.GetHashAndReset();
    }
}
