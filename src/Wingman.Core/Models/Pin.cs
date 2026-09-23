namespace Wingman.Core.Models;

/// <summary>
/// One row from <c>winget pin list</c>.
/// </summary>
/// <param name="PinnedVersion">
/// The version or range the pin holds to; empty for pins that do not name one.
/// </param>
public sealed record Pin(
    string Id,
    string Name,
    string Version,
    string Source,
    PinType PinType,
    string PinnedVersion);
