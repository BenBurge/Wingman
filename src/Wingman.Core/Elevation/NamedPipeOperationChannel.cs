using System.Text;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Elevation;

/// <summary>
/// Speaks <see cref="ElevatedWorkerProtocol"/> over a connected duplex stream, in production the
/// server end of an <see cref="ElevatedWorkerPipe"/> after the helper has connected.
/// </summary>
/// <remarks>
/// The channel owns the stream it is given and disposes it. Canceling <see cref="RunAsync"/>
/// mid-operation leaves the helper's remaining output unread, so dispose the channel afterward
/// rather than running another operation on it.
/// </remarks>
public sealed class NamedPipeOperationChannel : IElevatedOperationChannel
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NamedPipeOperationChannel(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true) { NewLine = "\n" };
    }

    public async Task<OperationResult> RunAsync(
        OperationKind kind,
        OperationRequest request,
        IProgress<string> output,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await WriteAsync(new WorkerMessage.RunOperation(kind, request), ct);

            var log = new List<string>();
            while (true)
            {
                var message = await ReadAsync(ct);
                switch (message)
                {
                    case WorkerMessage.Line line:
                        log.Add(line.Text);
                        output.Report(line.Text);
                        break;

                    case WorkerMessage.Finished finished:
                        var succeeded = finished.ExitCode == 0;
                        var duration = TimeSpan.FromMilliseconds(finished.DurationMs);
                        return new OperationResult(finished.ExitCode, succeeded, duration, log);

                    default:
                        throw new FormatException(
                            $"Elevated helper sent an unexpected {message.GetType().Name} message.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await WriteAsync(new WorkerMessage.Shutdown(), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // The reader and writer leave the stream open and hold nothing else, so disposing the
        // stream is enough; disposing the writer would flush into a pipe that may be broken.
        await _stream.DisposeAsync();
        _gate.Dispose();
    }

    private async Task WriteAsync(WorkerMessage message, CancellationToken ct)
    {
        var line = ElevatedWorkerProtocol.Serialize(message);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), ct);
            await _writer.FlushAsync(ct);
        }
        catch (IOException ex)
        {
            throw Disconnected(ex);
        }
    }

    private async Task<WorkerMessage> ReadAsync(CancellationToken ct)
    {
        string? line;
        try
        {
            line = await _reader.ReadLineAsync(ct);
        }
        catch (IOException ex)
        {
            throw Disconnected(ex);
        }

        if (line is null)
        {
            throw Disconnected(null);
        }

        return ElevatedWorkerProtocol.Parse(line);
    }

    private static IOException Disconnected(Exception? inner) => new("Elevated helper disconnected", inner);
}
