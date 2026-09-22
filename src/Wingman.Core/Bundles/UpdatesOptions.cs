namespace Wingman.Core.Bundles;

/// <summary>
/// Per-package update options, mirroring UniGetUI's <c>UpdatesOptions</c> schema verbatim.
/// </summary>
public sealed class UpdatesOptions
{
    public bool UpdatesIgnored { get; set; }

    public string IgnoredVersion { get; set; } = "";

    public bool IsDefault() => Equals(new UpdatesOptions());

    public override bool Equals(object? obj) =>
        obj is UpdatesOptions other
        && UpdatesIgnored == other.UpdatesIgnored
        && IgnoredVersion == other.IgnoredVersion;

    public override int GetHashCode() => HashCode.Combine(UpdatesIgnored, IgnoredVersion);
}
