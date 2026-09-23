using System.Diagnostics;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_Canceled_KillsTheProcess()
    {
        var runner = new ProcessRunner();
        var (fileName, arguments) = LongRunningCommand();
        var processName = Path.GetFileNameWithoutExtension(fileName);
        var testStart = DateTime.Now;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var runTask = runner.RunAsync(fileName, arguments, cts.Token);

        // ProcessRunner surfaces cancellation as TaskCanceledException, a subclass of
        // OperationCanceledException; assert on the base type rather than the exact one.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (HasProcessStartedAfter(processName, testStart) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.False(HasProcessStartedAfter(processName, testStart));
    }

    // Other, unrelated ping/sleep processes may already be running on the machine; only one
    // started after this test began can be the one the runner spawned and should have killed.
    private static bool HasProcessStartedAfter(string processName, DateTime testStart)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.StartTime >= testStart)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process already exited between GetProcessesByName and this check.
                }
            }
        }

        return false;
    }

    // A process short enough to finish on its own would make the test racy about whether
    // cancellation or a natural exit killed it; `ping`/`sleep` run long past the 200 ms cancel.
    private static (string FileName, string[] Arguments) LongRunningCommand() =>
        OperatingSystem.IsWindows()
            ? ("ping", ["-n", "30", "127.0.0.1"])
            : ("sleep", ["30"]);
}
