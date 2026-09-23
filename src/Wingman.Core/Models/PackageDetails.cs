namespace Wingman.Core.Models;

/// <summary>
/// The manifest details <c>winget show</c> prints for one package. Every string is empty when
/// winget did not print that field.
/// </summary>
public sealed class PackageDetails
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Version { get; init; } = "";

    public string Publisher { get; init; } = "";

    public string PublisherUrl { get; init; } = "";

    public string Author { get; init; } = "";

    public string Moniker { get; init; } = "";

    /// <summary>
    /// The description, with line breaks kept as <c>\n</c> when winget printed it over several lines.
    /// </summary>
    public string Description { get; init; } = "";

    public string Homepage { get; init; } = "";

    public string License { get; init; } = "";

    public string LicenseUrl { get; init; } = "";

    public string PrivacyUrl { get; init; } = "";

    /// <summary>
    /// The release notes, with line breaks kept as <c>\n</c> when winget printed them over several lines.
    /// </summary>
    public string ReleaseNotes { get; init; } = "";

    public string ReleaseNotesUrl { get; init; } = "";

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string InstallerType { get; init; } = "";

    public string InstallerUrl { get; init; } = "";

    public string InstallerSha256 { get; init; } = "";

    public string InstallerLocale { get; init; } = "";

    public string InstallerSize { get; init; } = "";

    /// <summary>
    /// Every field winget printed that has no property above, keyed by the label exactly as winget
    /// printed it (for example <c>Publisher Support Url</c>). Fields nested under <c>Installer:</c>
    /// are keyed <c>Installer.&lt;label&gt;</c>, for example <c>Installer.Release Date</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalFields { get; init; } = new Dictionary<string, string>();
}
