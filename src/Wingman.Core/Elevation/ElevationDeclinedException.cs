namespace Wingman.Core.Elevation;

/// <summary>
/// The user declined the elevation prompt that would have started the elevated helper, or the
/// helper exited before it connected, which is how a declined Admin By Request prompt shows up.
/// </summary>
public sealed class ElevationDeclinedException : Exception
{
    public ElevationDeclinedException()
        : base("Canceled by UAC")
    {
    }

    public ElevationDeclinedException(string message)
        : base(message)
    {
    }
}
