using System.Globalization;

namespace Wingman.Core.Elevation;

/// <summary>
/// The elevated helper did not connect within <see cref="Timeout"/>: the elevation prompt was left
/// unanswered, or the helper started and never reached the pipe.
/// </summary>
public sealed class ElevationTimedOutException : TimeoutException
{
    public ElevationTimedOutException(TimeSpan timeout)
        : base($"No connection from the elevated helper after {Seconds(timeout)} s")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    /// <summary>Whole seconds, as the batch screen and the message show them.</summary>
    public static string Seconds(TimeSpan timeout) =>
        ((long)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
