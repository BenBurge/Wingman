using System.IO.Pipes;
using System.Text;
using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Tests;

public sealed class NamedPipeOperationChannelTests : IAsyncDisposable
{
    // A protocol bug would otherwise hang the suite on a read that never completes.
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));

    private readonly NamedPipeServerStream _server;
    private readonly NamedPipeClientStream _client;

    public NamedPipeOperationChannelTests()
    {
        var name = ElevatedWorkerPipe.NewPipeName();
        _server = ElevatedWorkerPipe.CreateServer(name);
        _client = ElevatedWorkerPipe.CreateClient(name);
    }

    private CancellationToken Ct => _timeout.Token;

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        _timeout.Dispose();
    }

    [Fact]
    public async Task RunAsync_HelperFinishes_ReturnsResultAndForwardsLines()
    {
        await ConnectAsync();
        await using var channel = new NamedPipeOperationChannel(_server);
        var helper = RunFakeHelperAsync(async (reader, writer) =>
        {
            var run = Assert.IsType<WorkerMessage.RunOperation>(await ReadMessageAsync(reader));
            Assert.Equal(OperationKind.Install, run.Kind);
            Assert.Equal("Git.Git", run.Request.Id);

            await WriteMessageAsync(writer, new WorkerMessage.Line("Downloading"));
            await WriteMessageAsync(writer, new WorkerMessage.Line("Successfully installed"));
            await WriteMessageAsync(writer, new WorkerMessage.Finished(0, 1234));
        });
        var output = new RecordingProgress();

        var result = await channel.RunAsync(OperationKind.Install, new OperationRequest("Git.Git"), output, Ct);
        await helper;

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Succeeded);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), result.Duration);
        Assert.Equal(["Downloading", "Successfully installed"], result.Log);
        Assert.Equal(["Downloading", "Successfully installed"], output.Lines);
    }

    [Fact]
    public async Task RunAsync_HelperDisconnectsBeforeFinished_ThrowsIOException()
    {
        await ConnectAsync();
        await using var channel = new NamedPipeOperationChannel(_server);
        var helper = RunFakeHelperAsync(async (reader, writer) =>
        {
            await ReadMessageAsync(reader);
            await WriteMessageAsync(writer, new WorkerMessage.Line("Downloading"));
            await _client.DisposeAsync();
        });

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            channel.RunAsync(OperationKind.Install, new OperationRequest("Git.Git"), new RecordingProgress(), Ct));
        await helper;

        Assert.Equal("Elevated helper disconnected", ex.Message);
    }

    [Fact]
    public async Task RunAsync_HelperReturnsNonZeroExitCode_ReportsFailure()
    {
        await ConnectAsync();
        await using var channel = new NamedPipeOperationChannel(_server);
        var helper = RunFakeHelperAsync(async (reader, writer) =>
        {
            await ReadMessageAsync(reader);
            await WriteMessageAsync(writer, new WorkerMessage.Line("Installer failed with exit code: 1603"));
            await WriteMessageAsync(writer, new WorkerMessage.Finished(1603, 50));
        });

        var result = await channel.RunAsync(
            OperationKind.Upgrade, new OperationRequest("Git.Git"), new RecordingProgress(), Ct);
        await helper;

        Assert.Equal(1603, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Equal(["Installer failed with exit code: 1603"], result.Log);
    }

    [Fact]
    public async Task ShutdownAsync_WritesShutdownLine()
    {
        await ConnectAsync();
        await using var channel = new NamedPipeOperationChannel(_server);
        var helper = RunFakeHelperAsync(async (reader, _) =>
            Assert.IsType<WorkerMessage.Shutdown>(await ReadMessageAsync(reader)));

        await channel.ShutdownAsync(Ct);
        await helper;
    }

    private async Task ConnectAsync() =>
        await Task.WhenAll(_server.WaitForConnectionAsync(Ct), _client.ConnectAsync(Ct));

    private Task RunFakeHelperAsync(Func<StreamReader, StreamWriter, Task> script) => Task.Run(async () =>
    {
        // Not disposed: disposing a StreamWriter flushes the stream, which throws once a script
        // has closed the pipe. DisposeAsync closes the pipe itself.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var reader = new StreamReader(_client, utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writer = new StreamWriter(_client, utf8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
        await script(reader, writer);
    });

    private async Task<WorkerMessage> ReadMessageAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync(Ct);
        Assert.NotNull(line);
        return ElevatedWorkerProtocol.Parse(line);
    }

    private async Task WriteMessageAsync(StreamWriter writer, WorkerMessage message) =>
        await writer.WriteLineAsync(ElevatedWorkerProtocol.Serialize(message).AsMemory(), Ct);
}
