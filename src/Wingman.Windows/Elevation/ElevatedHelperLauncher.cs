using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Wingman.Core.Elevation;

namespace Wingman.Windows.Elevation;

/// <summary>
/// Starts <c>wingman --elevated-worker</c> through a UAC prompt and connects to it. A batch calls
/// this once, so the user sees one prompt per batch however many operations need elevation.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedHelperLauncher
{
    private const int ErrorCancelled = 1223;

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(120);

    /// <inheritdoc cref="StartAsync(TimeSpan, CancellationToken)"/>
    public static Task<IElevatedOperationChannel> StartAsync(CancellationToken ct) =>
        StartAsync(DefaultConnectTimeout, ct);

    /// <summary>
    /// Prompts for elevation and waits for the helper to connect. <c>Process.Start</c> runs on the
    /// thread pool, because ShellExecute blocks for as long as a UAC broker such as Admin By
    /// Request keeps its prompt up, and the wait must still end on a cancel or a timeout.
    /// </summary>
    /// <exception cref="ElevationDeclinedException">The user declined the prompt, or the helper
    /// exited before it connected.</exception>
    /// <exception cref="ElevationTimedOutException">The helper did not connect within
    /// <paramref name="connectTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled first.</exception>
    public static async Task<IElevatedOperationChannel> StartAsync(TimeSpan connectTimeout, CancellationToken ct)
    {
        var name = ElevatedWorkerPipe.NewPipeName();
        var server = ElevatedWorkerPipe.CreateServer(name);
        var start = Task.Run(() => StartHelper(name), CancellationToken.None);

        // Ends whichever of the two waits is still open once the race is decided.
        using var waits = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var connected = server.WaitForConnectionAsync(waits.Token);
            var exited = WaitForExitAsync(start, waits.Token);
            await ElevatedHelperConnection.WaitAsync(connected, exited, connectTimeout, ct);
        }
        catch
        {
            await waits.CancelAsync();
            await server.DisposeAsync();
            AbandonHelper(start, kill: true);
            throw;
        }

        await waits.CancelAsync();
        AbandonHelper(start, kill: false);
        return new NamedPipeOperationChannel(server);
    }

    private static Process? StartHelper(string pipeName)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--elevated-worker", pipeName },
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new ElevationDeclinedException();
        }
    }

    /// <summary>
    /// Completes when the helper exits, and faults as <paramref name="start"/> does. ShellExecute
    /// can hand back no process at all, and then only the connection or the timeout ends the wait.
    /// </summary>
    private static async Task WaitForExitAsync(Task<Process?> start, CancellationToken ct)
    {
        var helper = await start.WaitAsync(ct);
        if (helper is null)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        await helper.WaitForExitAsync(ct);
    }

    /// <summary>
    /// Releases the helper's process handle once <paramref name="start"/> has one, which may be
    /// after this returns when ShellExecute is still blocked, killing the helper first when asked.
    /// </summary>
    private static void AbandonHelper(Task<Process?> start, bool kill)
    {
        _ = start.ContinueWith(
            task =>
            {
                // Read so a start that failed after the wait ended is observed rather than left to the finalizer.
                _ = task.Exception;
                if (!task.IsCompletedSuccessfully || task.Result is not { } helper)
                {
                    return;
                }

                if (kill)
                {
                    KillIfRunning(helper);
                }

                helper.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Best effort: the handle ShellExecute returns for an elevated process may not grant
    // terminate access, and the helper exits by itself when its own connect times out.
    private static void KillIfRunning(Process helper)
    {
        try
        {
            if (!helper.HasExited)
            {
                helper.Kill();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
        }
    }
}
