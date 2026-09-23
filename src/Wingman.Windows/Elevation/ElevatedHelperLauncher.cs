using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Wingman.Core.Elevation;

namespace Wingman.Windows.Elevation;

/// <summary>
/// Starts <c>wingman --elevated-worker</c> through a UAC prompt and connects to it. A batch calls
/// this once, so the user sees one prompt per batch however many operations need elevation.
/// </summary>
/// <remarks>
/// The helper runs whatever arrives on the pipe with administrator rights, so both ends check
/// the other. The pipe's ACL admits only the current user and Administrators, which keeps other
/// local users from connecting first or reading it. The helper is told this process's id with
/// <c>--parent</c> and refuses a server with any other id, so a process that creates a pipe of
/// the same name after this one has given up cannot feed it operations. This process in turn
/// refuses a client whose id is not the helper it started. The helper connects at the
/// Identification impersonation level, so even a server that got past those checks could learn
/// who the helper is but never act with its elevated token.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ElevatedHelperLauncher
{
    private const string UnexpectedClientMessage = "Unexpected process connected to the elevation pipe";

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
    /// <exception cref="ElevationDeclinedException">The user declined the prompt, the helper
    /// exited before it connected, or a process other than the helper connected.</exception>
    /// <exception cref="ElevationTimedOutException">The helper did not connect within
    /// <paramref name="connectTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled first.</exception>
    public static async Task<IElevatedOperationChannel> StartAsync(TimeSpan connectTimeout, CancellationToken ct)
    {
        var name = ElevatedWorkerPipe.NewPipeName();
        var server = SecurePipeServer.Create(name);
        var start = Task.Run(() => StartHelper(name), CancellationToken.None);

        // Ends whichever of the two waits is still open once the race is decided.
        using var waits = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var connected = server.WaitForConnectionAsync(waits.Token);
            var exited = WaitForExitAsync(start, waits.Token);
            await ElevatedHelperConnection.WaitAsync(connected, exited, connectTimeout, ct);
            await VerifyClientIsHelperAsync(server, start, connectTimeout, ct);
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
            var parentId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            return Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--elevated-worker", pipeName, "--parent", parentId },
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
    /// Throws unless the connected client is the process <paramref name="start"/> created. The
    /// helper can only connect once ShellExecute has created it, so its handle is at most moments
    /// away. A client that connects while the prompt is still up is someone else, so the wait for
    /// the handle is bounded, and a client that cannot be matched to a handle is refused.
    /// </summary>
    private static async Task VerifyClientIsHelperAsync(
        NamedPipeServerStream server, Task<Process?> start, TimeSpan timeout, CancellationToken ct)
    {
        Process? helper;
        try
        {
            helper = await start.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            helper = null;
        }

        var clientKnown = PipeNativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientProcessId);
        var clientIsHelper = clientKnown && helper is not null && clientProcessId == (uint)helper.Id;
        if (!clientIsHelper)
        {
            throw new ElevationDeclinedException(UnexpectedClientMessage);
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
    // terminate access, and the helper exits by itself when its own connect times out. The whole
    // tree goes, so a winget the helper already started does not outlive it.
    private static void KillIfRunning(Process helper)
    {
        try
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
        {
        }
    }
}
