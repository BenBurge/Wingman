namespace Wingman.Core.Elevation;

/// <summary>
/// The launcher's wait for the elevated helper to connect, kept apart from the Windows process
/// and pipe so it can be tested with plain tasks.
/// </summary>
public static class ElevatedHelperConnection
{
    public const string ExitedBeforeConnectingMessage =
        "The elevation request was declined or the helper exited before connecting";

    /// <summary>
    /// Returns once <paramref name="connected"/> completes, whichever of the others is still open.
    /// </summary>
    /// <param name="connected">Completes when the helper connects to the pipe.</param>
    /// <param name="exited">Completes when the helper process exits. A fault is rethrown as is, so
    /// the launcher can fail it with the reason the process could not be started.</param>
    /// <exception cref="ElevationDeclinedException"><paramref name="exited"/> completed first.</exception>
    /// <exception cref="ElevationTimedOutException">Neither completed within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled first.</exception>
    public static async Task WaitAsync(Task connected, Task exited, TimeSpan timeout, CancellationToken ct)
    {
        using var stopTimer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(timeout, stopTimer.Token);

        // Listed first so a helper that connected and exited in the same instant counts as connected.
        var first = await Task.WhenAny(connected, exited, timer);
        await stopTimer.CancelAsync();

        if (first == connected)
        {
            await connected;
            return;
        }

        ct.ThrowIfCancellationRequested();
        if (first == exited)
        {
            await exited;
            throw new ElevationDeclinedException(ExitedBeforeConnectingMessage);
        }

        throw new ElevationTimedOutException(timeout);
    }
}
