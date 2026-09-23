namespace Wingman.Core.Elevation;

/// <summary>
/// The user declined the UAC prompt that would have started the elevated helper.
/// </summary>
public sealed class ElevationDeclinedException : Exception
{
    public ElevationDeclinedException()
        : base("Canceled by UAC")
    {
    }
}
