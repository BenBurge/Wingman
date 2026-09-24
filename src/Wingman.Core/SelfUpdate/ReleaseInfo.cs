namespace Wingman.Core.SelfUpdate;

/// <summary>
/// The latest Wingman release on GitHub, reduced to what self-update needs: the installer for one
/// runtime and the <c>.sha256</c> file published beside it.
/// </summary>
/// <param name="Version">The tag without its leading <c>v</c>, such as <c>1.2.0</c>.</param>
/// <param name="TagName">The tag as published, such as <c>v1.2.0</c>.</param>
/// <param name="SetupUrl">Where <c>wingman-v&lt;version&gt;-&lt;rid&gt;-setup.exe</c> downloads from.</param>
/// <param name="Sha256Url">Where that installer's <c>.sha256</c> file downloads from.</param>
/// <param name="ReleaseNotesUrl">The release's page on GitHub.</param>
public sealed record ReleaseInfo(string Version, string TagName, Uri SetupUrl, Uri Sha256Url, string ReleaseNotesUrl);
