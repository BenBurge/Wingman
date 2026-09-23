using System.Runtime.Versioning;
using Wingman.Core.Elevation;
using Wingman.Core.Winget;

namespace Wingman.Windows.Elevation;

/// <summary>
/// The body of <c>wingman --elevated-worker &lt;pipe-name&gt;</c>, the process
/// <see cref="ElevatedHelperLauncher"/> starts elevated: connects to the TUI's pipe and serves
/// operations with the real winget until told to stop.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedWorker
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns the process exit code: 0 after a <c>shutdown</c> message or a closed pipe, 1 when
    /// the pipe never connects or the conversation breaks, with the reason written to stderr.
    /// </summary>
    public static async Task<int> RunAsync(string pipeName, CancellationToken ct)
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
}
