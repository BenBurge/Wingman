using System.Text.Json.Serialization;

namespace Wingman.Core.Settings;

/// <summary>
/// When a batch runs its operations through the elevated helper.
/// </summary>
public enum ElevationMode
{
    /// <summary>
    /// Uses the helper for operations whose plan needs elevation or whose installer type usually
    /// does, behind one prompt for the batch.
    /// </summary>
    [JsonStringEnumMemberName("auto")]
    Auto,

    /// <summary>Runs every operation through the helper, behind one prompt for the batch.</summary>
    [JsonStringEnumMemberName("always")]
    Always,

    /// <summary>Never launches the helper; winget and each installer prompt on their own.</summary>
    [JsonStringEnumMemberName("never")]
    Never,
}
