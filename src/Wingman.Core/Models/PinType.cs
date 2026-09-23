namespace Wingman.Core.Models;

/// <summary>
/// The kinds of pin <c>winget pin</c> supports.
/// </summary>
public enum PinType
{
    /// <summary>Excluded from <c>upgrade --all</c> but can still be upgraded by id.</summary>
    Pinning,

    /// <summary>Never upgraded until the pin is removed.</summary>
    Blocking,

    /// <summary>Upgraded only within the pinned version range, such as <c>1.2.*</c>.</summary>
    Gating,
}
