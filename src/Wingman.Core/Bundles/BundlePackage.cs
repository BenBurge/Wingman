using System.Text.Json.Serialization;

namespace Wingman.Core.Bundles;

/// <summary>
/// One package entry in a UniGetUI bundle. Property names and casing match UniGetUI's own
/// <c>Package</c> bundle schema verbatim so bundles round-trip between the two tools.
/// </summary>
public sealed class BundlePackage
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Version { get; set; } = "";

    public string Source { get; set; } = "";

    public string ManagerName { get; set; } = "";

    /// <summary>
    /// Null when the package uses default install options, so the JSON omits the key entirely
    /// (matching what UniGetUI itself writes for packages nobody has customized).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallOptions? InstallationOptions { get; set; }

    /// <summary>
    /// Null when the package uses default update options; see <see cref="InstallationOptions"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UpdatesOptions? Updates { get; set; }
}
