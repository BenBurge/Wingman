using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
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

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

    /// <exception cref="ElevationDeclinedException">The user declined the UAC prompt.</exception>
    /// <exception cref="TimeoutException">The helper started but did not connect in time.</exception>
    public static async Task<IElevatedOperationChannel> StartAsync(CancellationToken ct)
    {
        var name = ElevatedWorkerPipe.NewPipeName();
        var server = ElevatedWorkerPipe.CreateServer(name);

        Process? helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--elevated-worker", name },
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            await server.DisposeAsync();
            throw new ElevationDeclinedException();
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        using (helper)
        {
            await WaitForHelperAsync(server, helper, ct);
        }

        return new NamedPipeOperationChannel(server);
    }

    private static async Task WaitForHelperAsync(NamedPipeServerStream server, Process? helper, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await server.WaitForConnectionAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            KillIfRunning(helper);
            await server.DisposeAsync();
            throw new TimeoutException("Elevated helper did not connect");
        }
        catch
        {
            KillIfRunning(helper);
            await server.DisposeAsync();
            throw;
        }
    }

    // Best effort: the handle ShellExecute returns for an elevated process may not grant
    // terminate access, and the helper exits by itself when its own connect times out.
    private static void KillIfRunning(Process? helper)
    {
        try
        {
            if (helper is { HasExited: false })
            {
                helper.Kill();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
        }
    }
}
