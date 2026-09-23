using Wingman.Core.Elevation;

namespace Wingman.Core.Tests;

public class ElevatedHelperConnectionTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(5);

    private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task WaitAsync_Connected_Returns()
    {
        var wait = ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, CancellationToken.None);

        _connected.SetResult();

        await wait;
    }

    [Fact]
    public async Task WaitAsync_ConnectedAndExitedTogether_CountsAsConnected()
    {
        _connected.SetResult();
        _exited.SetResult();

        await ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, CancellationToken.None);
    }

    [Fact]
    public async Task WaitAsync_ExitedBeforeConnecting_ThrowsDeclined()
    {
        var wait = ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, CancellationToken.None);

        _exited.SetResult();

        var declined = await Assert.ThrowsAsync<ElevationDeclinedException>(() => wait);
        Assert.Equal("The elevation request was declined or the helper exited before connecting", declined.Message);
    }

    [Fact]
    public async Task WaitAsync_ExitFaults_RethrowsItsException()
    {
        var wait = ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, CancellationToken.None);

        _exited.SetException(new ElevationDeclinedException());

        var declined = await Assert.ThrowsAsync<ElevationDeclinedException>(() => wait);
        Assert.Equal("Canceled by UAC", declined.Message);
    }

    [Fact]
    public async Task WaitAsync_ConnectionFaults_RethrowsItsException()
    {
        var wait = ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, CancellationToken.None);

        _connected.SetException(new IOException("pipe broke"));

        var error = await Assert.ThrowsAsync<IOException>(() => wait);
        Assert.Equal("pipe broke", error.Message);
    }

    [Fact]
    public async Task WaitAsync_NeitherInTime_ThrowsTimedOutWithTheTimeout()
    {
        var timeout = TimeSpan.FromMilliseconds(50);

        var timedOut = await Assert.ThrowsAsync<ElevationTimedOutException>(
            () => ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, timeout, CancellationToken.None));

        Assert.Equal(timeout, timedOut.Timeout);
    }

    [Fact]
    public void ElevationTimedOutException_Message_NamesWholeSeconds()
    {
        var timedOut = new ElevationTimedOutException(TimeSpan.FromSeconds(120));

        Assert.Equal("No connection from the elevated helper after 120 s", timedOut.Message);
    }

    [Fact]
    public async Task WaitAsync_Canceled_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = ElevatedHelperConnection.WaitAsync(_connected.Task, _exited.Task, LongTimeout, cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
