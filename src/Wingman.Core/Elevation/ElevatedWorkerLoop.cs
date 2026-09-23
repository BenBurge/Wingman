using System.Diagnostics;
using System.Text;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Core.Elevation;

/// <summary>
/// The elevated helper's side of <see cref="ElevatedWorkerProtocol"/>: reads <c>run</c> messages
/// from a connected stream, runs each through an <see cref="IWingetClient"/>, and streams the
/// output lines and a <c>finished</c> message back. The counterpart of
/// <see cref="NamedPipeOperationChannel"/>.
/// </summary>
public static class ElevatedWorkerLoop
{
    private const int FailedExitCode = -1;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Serves operations until the other end sends <c>shutdown</c> or closes the stream. A client
    /// exception ends only that operation, as a <c>Failed:</c> line and exit code -1, so the TUI
    /// never waits on a <c>finished</c> that will not come.
    /// </summary>
    /// <exception cref="FormatException">The other end sent a line that is not a valid request.</exception>
    /// <exception cref="IOException">The stream broke before a <c>finished</c> message could be written.</exception>
    public static async Task RunAsync(Stream pipe, IWingetClient client, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writer = new MessageWriter(pipe);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                return;
            }

            var message = ElevatedWorkerProtocol.Parse(line);
            switch (message)
            {
                case WorkerMessage.RunOperation run:
                    await RunOperationAsync(run, client, writer, ct);
                    break;

                case WorkerMessage.Shutdown:
                    return;

                default:
                    throw new FormatException($"Elevated helper received an unexpected {message.GetType().Name} message.");
            }
        }
    }

    private static async Task RunOperationAsync(
        WorkerMessage.RunOperation run, IWingetClient client, MessageWriter writer, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = new LineForwarder(writer);
        int exitCode;
        try
        {
            var result = await RunClientAsync(run.Kind, run.Request, client, output, ct);
            exitCode = result.ExitCode;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            output.Report($"Failed: {ex.Message}");
            exitCode = FailedExitCode;
        }

        writer.Write(new WorkerMessage.Finished(exitCode, (long)stopwatch.Elapsed.TotalMilliseconds));
    }

    private static Task<OperationResult> RunClientAsync(
        OperationKind kind, OperationRequest request, IWingetClient client, IProgress<string> output, CancellationToken ct) =>
        kind switch
        {
            OperationKind.Install => client.InstallAsync(request, output, ct),
            OperationKind.Upgrade => client.UpgradeAsync(request, output, ct),
            OperationKind.Uninstall => client.UninstallAsync(request, output, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>
    /// Writes one message per line and flushes it at once so the TUI sees output live. Locked
    /// because <see cref="ProcessRunner"/> reports stdout and stderr lines from separate threads.
    /// </summary>
    private sealed class MessageWriter
    {
        private readonly StreamWriter _writer;
        private readonly Lock _lock = new();

        public MessageWriter(Stream pipe)
        {
            _writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { NewLine = "\n" };
        }

        public void Write(WorkerMessage message)
        {
            var line = ElevatedWorkerProtocol.Serialize(message);
            lock (_lock)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }
    }

    /// <summary>
    /// Sends each output line as a <c>line</c> message. A broken pipe drops the remaining lines
    /// instead of throwing, because the throw would land on the process runner's event thread and
    /// crash the helper; the <c>finished</c> write that follows surfaces the break instead.
    /// </summary>
    private sealed class LineForwarder : IProgress<string>
    {
        private readonly MessageWriter _writer;
        private volatile bool _broken;

        public LineForwarder(MessageWriter writer)
        {
            _writer = writer;
        }

        public void Report(string value)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                _writer.Write(new WorkerMessage.Line(value));
            }
            catch (IOException)
            {
                _broken = true;
            }
        }
    }
}
