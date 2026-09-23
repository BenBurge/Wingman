using System.IO.Pipes;
using System.Text;
using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

/// <summary>
/// Runs <see cref="ElevatedWorkerLoop"/> on one end of an in-process pipe and talks to it from the
/// other through <see cref="NamedPipeOperationChannel"/>, the way the TUI talks to the real helper.
/// </summary>
public sealed class ElevatedWorkerLoopTests : IAsyncDisposable
{
    private const string InstallableId = "Axosoft.GitKraken";
    private const string FailingId = "Vendor.WillFail";

    // A protocol bug would otherwise hang the suite on a read that never completes.
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));

    private readonly NamedPipeServerStream _server;
    private readonly NamedPipeClientStream _client;

    public ElevatedWorkerLoopTests()
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
    public async Task RunOperation_ThenShutdown_StreamsClientOutputAndEndsLoop()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new FakeWingetClient(TimeSpan.Zero), Ct);
        await using var channel = new NamedPipeOperationChannel(_server);
        var request = new OperationRequest(InstallableId);
        var expected = new RecordingProgress();
        await new FakeWingetClient(TimeSpan.Zero).InstallAsync(request, expected, Ct);
        var output = new RecordingProgress();

        var result = await channel.RunAsync(OperationKind.Install, request, output, Ct);
        await channel.ShutdownAsync(Ct);
        await worker;

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Succeeded);
        Assert.NotEmpty(expected.Lines);
        Assert.Equal(expected.Lines, output.Lines);
        Assert.Equal(expected.Lines, result.Log);
    }

    [Fact]
    public async Task RunOperation_InstallerFails_ReturnsItsExitCode()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new FakeWingetClient(TimeSpan.Zero), Ct);
        await using var channel = new NamedPipeOperationChannel(_server);

        var result = await channel.RunAsync(
            OperationKind.Install, new OperationRequest(FailingId), new RecordingProgress(), Ct);
        await channel.ShutdownAsync(Ct);
        await worker;

        Assert.Equal(1603, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunOperation_ClientThrows_ReportsFailedLineAndKeepsServing()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new ThrowingWingetClient(), Ct);
        await using var channel = new NamedPipeOperationChannel(_server);

        var first = await channel.RunAsync(
            OperationKind.Upgrade, new OperationRequest(InstallableId), new RecordingProgress(), Ct);
        var second = await channel.RunAsync(
            OperationKind.Uninstall, new OperationRequest(InstallableId), new RecordingProgress(), Ct);
        await channel.ShutdownAsync(Ct);
        await worker;

        Assert.Equal(-1, first.ExitCode);
        Assert.Equal([$"Failed: {ThrowingWingetClient.Message}"], first.Log);
        Assert.Equal(-1, second.ExitCode);
    }

    [Fact]
    public async Task OtherEndCloses_EndsLoopWithoutError()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new FakeWingetClient(TimeSpan.Zero), Ct);

        await _server.DisposeAsync();

        await worker;
    }

    [Fact]
    public async Task UnparseableLine_ThrowsFormatException()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new FakeWingetClient(TimeSpan.Zero), Ct);

        await WriteRawLineAsync("not json");

        await Assert.ThrowsAsync<FormatException>(() => worker);
    }

    [Fact]
    public async Task HelperToTuiMessage_ThrowsFormatException()
    {
        await ConnectAsync();
        var worker = ElevatedWorkerLoop.RunAsync(_client, new FakeWingetClient(TimeSpan.Zero), Ct);

        await WriteRawLineAsync(ElevatedWorkerProtocol.Serialize(new WorkerMessage.Finished(0, 1)));

        await Assert.ThrowsAsync<FormatException>(() => worker);
    }

    private async Task ConnectAsync() =>
        await Task.WhenAll(_server.WaitForConnectionAsync(Ct), _client.ConnectAsync(Ct));

    private async Task WriteRawLineAsync(string line)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(line + "\n");
        await _server.WriteAsync(bytes, Ct);
        await _server.FlushAsync(Ct);
    }

    /// <summary>
    /// Fails every operation with an exception, as a missing <c>winget.exe</c> would.
    /// </summary>
    private sealed class ThrowingWingetClient : IWingetClient
    {
        public const string Message = "winget is not installed";

        public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            Fail();

        public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            Fail();

        public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
            Fail();

        public Task<string> GetVersionAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<PackageDetails?> ShowAsync(string id, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) => throw new NotSupportedException();

        private static Task<OperationResult> Fail() =>
            Task.FromException<OperationResult>(new InvalidOperationException(Message));
    }
}
