using System.Text.Json.Serialization;

namespace Wingman.Core.Bundles;

/// <summary>
/// A package entry from a bundle that could not be imported because it belongs to a different
/// package manager than the one that produced this Wingman install (currently always winget).
/// </summary>
public sealed class IncompatiblePackage
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Version { get; set; } = "";

    public string Source { get; set; } = "";
}

/// <summary>
/// A UniGetUI-compatible package bundle (<c>.ubundle</c>). Property names and casing match UniGetUI's
/// own bundle schema verbatim, including its snake_case top-level keys, so bundles exported by Wingman
/// can be imported by UniGetUI and vice versa.
/// </summary>
public sealed class Bundle
{
    [JsonPropertyName("export_version")]
    public double ExportVersion { get; set; } = 3;

    [JsonPropertyName("packages")]
    public List<BundlePackage> Packages { get; set; } = [];

    [JsonPropertyName("incompatible_packages_info")]
    public string IncompatiblePackagesInfo { get; set; } = "";

    [JsonPropertyName("incompatible_packages")]
    public List<IncompatiblePackage> IncompatiblePackages { get; set; } = [];
}
