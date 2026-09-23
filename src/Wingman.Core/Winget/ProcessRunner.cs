using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Wingman.Core.Winget;

/// <summary>
/// Runs an external process with <see cref="Process"/>, redirecting both output streams as UTF-8.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments) };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdOut.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdErr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    public async Task<int> RunStreamingAsync(
        string fileName, IReadOnlyList<string> arguments, IProgress<string> output, CancellationToken ct)
    {
        using var process = new Process { StartInfo = CreateStartInfo(fileName, arguments) };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.Report(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.Report(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // Also waits for both redirected streams to reach end of file, so every line has been
            // reported by the time this returns.
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        return process.ExitCode;
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process already exited, or a child (such as an elevated installer) cannot be
            // killed from here. Either way the caller should still see the cancellation.
        }
    }
}
