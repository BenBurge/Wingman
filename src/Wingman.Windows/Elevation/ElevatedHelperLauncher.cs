using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Wingman.Core.Elevation;
using Wingman.Core.Settings;

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
/// refuses a client that is not the very process it started, or, when PowerShell starts the
/// helper as its child, a client that is not this same executable in this same session. The
/// helper connects at the Identification impersonation level, so even a server that got past
/// those checks could learn who the helper is but never act with its elevated token.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ElevatedHelperLauncher
{
    private const string UnexpectedClientMessage = "Unexpected process connected to the elevation pipe";

    private const int ErrorCancelled = 1223;

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(120);

    /// <inheritdoc cref="StartAsync(ElevationLauncher, TimeSpan, CancellationToken)"/>
    public static Task<IElevatedOperationChannel> StartAsync(ElevationLauncher launcher, CancellationToken ct) =>
        StartAsync(launcher, DefaultConnectTimeout, ct);

    /// <summary>
    /// Prompts for elevation and waits for the helper to connect. <c>Process.Start</c> runs on the
    /// thread pool, because ShellExecute blocks for as long as a UAC broker such as Admin By
    /// Request keeps its prompt up, and the wait must still end on a cancel or a timeout.
    /// </summary>
    /// <param name="launcher">Whether the prompt is for <c>wingman.exe</c> itself or for the
    /// PowerShell that starts it; with PowerShell, the process started is the wrapper, which
    /// exits when the helper does.</param>
    /// <exception cref="ElevationDeclinedException">The user declined the prompt, the helper
    /// exited before it connected, or a process other than the helper connected.</exception>
    /// <exception cref="ElevationTimedOutException">The helper did not connect within
    /// <paramref name="connectTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled first.</exception>
    public static async Task<IElevatedOperationChannel> StartAsync(
        ElevationLauncher launcher, TimeSpan connectTimeout, CancellationToken ct)
    {
        var name = ElevatedWorkerPipe.NewPipeName();
        var server = SecurePipeServer.Create(name);
        var start = Task.Run(() => StartHelper(launcher, name), CancellationToken.None);

        // Ends whichever of the two waits is still open once the race is decided.
        using var waits = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var connected = server.WaitForConnectionAsync(waits.Token);
            var exited = WaitForExitAsync(start, waits.Token);
            await ElevatedHelperConnection.WaitAsync(connected, exited, connectTimeout, ct);
            await VerifyClientIsHelperAsync(server, start, launcher, connectTimeout, ct);
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

    /// <summary>
    /// True when the client of <paramref name="pipe"/> is the helper by <see cref="PipePeerCheck.IsHelper"/>:
    /// the started process itself for <see cref="ElevationLauncher.Direct"/>, or
    /// <paramref name="expectedImagePath"/> in this process's session for
    /// <see cref="ElevationLauncher.PowerShell"/>. A client whose id cannot be read is refused.
    /// </summary>
    internal static bool IsExpectedClient(
        SafePipeHandle pipe, ElevationLauncher launcher, int startedProcessId, string? expectedImagePath)
    {
        if (!PipeNativeMethods.GetNamedPipeClientProcessId(pipe, out var clientProcessId))
        {
            return false;
        }

        return PipePeerCheck.IsHelper(
            launcher, clientProcessId, startedProcessId, expectedImagePath,
            PipeNativeMethods.GetProcessImagePath, IsInOwnSession);
    }

    private static bool IsInOwnSession(uint processId) =>
        PipeNativeMethods.ProcessIdToSessionId(processId, out var session)
        && PipeNativeMethods.ProcessIdToSessionId((uint)Environment.ProcessId, out var ownSession)
        && session == ownSession;

    /// <summary>
    /// This executable's path as the kernel reports it, so it compares equal to the helper's even
    /// when Wingman was started through a link such as winget's <c>Links</c> folder, which
    /// <see cref="Environment.ProcessPath"/> would report unresolved.
    /// </summary>
    internal static string? OwnImagePath() =>
        PipeNativeMethods.GetProcessImagePath((uint)Environment.ProcessId) ?? Environment.ProcessPath;

    private static Process? StartHelper(ElevationLauncher launcher, string pipeName)
    {
        var parentId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var (fileName, arguments) = ElevationLaunch.Build(
            launcher, Environment.SystemDirectory, Environment.ProcessPath!,
            ["--elevated-worker", pipeName, "--parent", parentId], hidden: true);
        try
        {
            return Process.Start(new ProcessStartInfo(fileName)
            {
                Arguments = arguments,
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
    /// Throws unless the connected client is the helper <paramref name="start"/> launched. The
    /// helper can only connect once ShellExecute has created its process, or the PowerShell that
    /// starts it, so that handle is at most moments away. A client that connects while the prompt
    /// is still up is someone else, so the wait for the handle is bounded, and a client that
    /// connects without one is refused.
    /// </summary>
    private static async Task VerifyClientIsHelperAsync(
        NamedPipeServerStream server, Task<Process?> start, ElevationLauncher launcher, TimeSpan timeout, CancellationToken ct)
    {
        Process? started;
        try
        {
            started = await start.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            started = null;
        }

        var clientIsHelper = started is not null
            && IsExpectedClient(server.SafePipeHandle, launcher, started.Id, OwnImagePath());
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
    /// Releases the started process's handle once <paramref name="start"/> has one, which may be
    /// after this returns when ShellExecute is still blocked, killing it first when asked.
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
    // tree goes, so the helper under a PowerShell wrapper, or a winget the helper already
    // started, does not outlive it.
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
