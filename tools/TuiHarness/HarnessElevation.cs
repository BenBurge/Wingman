using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace TuiHarness;

/// <summary>
/// Stands in for the elevated helper launcher, so batches with elevated operations go through the
/// same factory path as the app without a UAC prompt: it connects at once to a channel that runs
/// each operation on the harness's own client, or, after <see cref="DeclineNext"/>, waits as long
/// as <see cref="DeclineDelay"/> with the prompt "up" and then reports it declined, once.
/// </summary>
internal sealed class HarnessElevation(IWingetClient client)
{
    public static readonly TimeSpan DeclineDelay = TimeSpan.FromMilliseconds(300);

    // Set on the UI thread and read on the batch's thread-pool task.
    private volatile bool _declineNext;

    /// <summary>Makes the next <see cref="StartAsync"/> a declined prompt.</summary>
    public void DeclineNext() => _declineNext = true;

    public async Task<IElevatedOperationChannel> StartAsync(CancellationToken ct)
    {
        if (_declineNext)
        {
            _declineNext = false;
            await Task.Delay(DeclineDelay, ct);
            throw new ElevationDeclinedException();
        }

        return new InProcessChannel(client);
    }

    private sealed class InProcessChannel(IWingetClient client) : IElevatedOperationChannel
    {
        public Task<OperationResult> RunAsync(
            OperationKind kind, OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            kind switch
            {
                OperationKind.Install => client.InstallAsync(request, output, ct),
                OperationKind.Upgrade => client.UpgradeAsync(request, output, ct),
                OperationKind.Uninstall => client.UninstallAsync(request, output, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
