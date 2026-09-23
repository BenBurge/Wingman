using System.IO.Pipes;
using System.Runtime.Versioning;
using Wingman.Core.Elevation;
using Wingman.Core.Winget;

namespace Wingman.Windows.Elevation;

/// <summary>
/// The body of <c>wingman --elevated-worker &lt;pipe-name&gt; --parent &lt;pid&gt;</c>, the process
/// <see cref="ElevatedHelperLauncher"/> starts elevated: connects to the TUI's pipe and serves
/// operations with the real winget until told to stop.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedWorker
{
    // The helper exists only once the elevation prompt is approved, and the TUI's pipe is already
    // waiting by then, so a short window is ample and keeps the name from being squatted for long.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Returns the process exit code: 0 after a <c>shutdown</c> message or a closed pipe, 1 when
    /// the pipe never connects, its server is not <paramref name="parentProcessId"/>, or the
    /// conversation breaks, with the reason written to stderr.
    /// </summary>
    /// <param name="parentProcessId">The launcher's process id; null skips the server check, with a
    /// warning, for a helper started without <c>--parent</c>.</param>
    public static async Task<int> RunAsync(string pipeName, int? parentProcessId, CancellationToken ct)
    {
        await using var pipe = ElevatedWorkerPipe.CreateClient(pipeName);
        try
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"wingman: elevated worker could not connect to pipe '{pipeName}'");
            return 1;
        }

        if (parentProcessId is null)
        {
            Console.Error.WriteLine("wingman: elevated worker: no --parent given, so the pipe server is not verified");
        }
        else if (!IsServer(pipe, parentProcessId.Value))
        {
            Console.Error.WriteLine("wingman: elevated worker: pipe server is not the Wingman process that launched it");
            return 1;
        }

        var client = new WingetCliClient(new ProcessRunner());
        try
        {
            await ElevatedWorkerLoop.RunAsync(pipe, client, ct);
            return 0;
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            Console.Error.WriteLine($"wingman: elevated worker stopped: {ex.Message}");
            return 1;
        }
    }

    // A server whose process id cannot be read is treated as a stranger.
    private static bool IsServer(NamedPipeClientStream pipe, int expectedProcessId) =>
        PipeNativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId)
        && serverProcessId == (uint)expectedProcessId;
}
