using Wingman.Core.Bundles;
using Wingman.Core.Elevation;
using Wingman.Core.History;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public sealed class BatchRunnerElevationTests : IDisposable
{
    // Neither is installed in FakeWingetClient's fixtures, so an install through the fake client
    // shows up in its installed list and one through the channel does not.
    private const string ElevatedId = "Axosoft.GitKraken";
    private const string UnelevatedId = "GitButler.GitButler";

    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeWingetClient _client = new(TimeSpan.Zero);
    private readonly FakeElevatedChannel _channel = new();
    private int _factoryCalls;

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private BatchRunner CreateRunner(Func<CancellationToken, Task<IElevatedOperationChannel>>? factory) =>
        new(_client, new PrePostCommandRunner(new FakeProcessRunner()), new HistoryStore(_directoryPath), factory);

    private Task<IElevatedOperationChannel> OpenFakeChannel(CancellationToken ct)
    {
        _factoryCalls++;
        return Task.FromResult<IElevatedOperationChannel>(_channel);
    }

    private Task<IElevatedOperationChannel> FailWith(Exception ex)
    {
        _factoryCalls++;
        return Task.FromException<IElevatedOperationChannel>(ex);
    }

    private static QueuedOperation Queue(string id, bool elevated, bool forced = false)
    {
        var row = new PackageRow(Name: $"{id} name", Id: id, Version: "1.0", AvailableVersion: null, Source: "winget");
        var options = new InstallOptions { RunAsAdministrator = elevated };
        var plan = OperationRequestFactory.Create(OperationKind.Install, row, new WingmanSettings(), options);
        Assert.Equal(elevated, plan.RequiresElevation);
        return new QueuedOperation(OperationKind.Install, row, plan with { ForceElevation = forced });
    }

    private static QueuedOperation[] ElevatedThenUnelevated() =>
        [Queue(ElevatedId, elevated: true), Queue(UnelevatedId, elevated: false)];

    private static List<string> ElevationStates(RecordingBatchProgress progress) =>
        progress.Events.OfType<ElevationState>().Select(e => e.State).ToList();

    private async Task<bool> IsInstalledAsync(string id)
    {
        var installed = await _client.ListInstalledAsync(CancellationToken.None);
        return installed.Any(row => row.Id == id);
    }

    [Fact]
    public async Task RunAsync_MixedQueue_RunsOnlyElevatedOperationThroughChannel()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(ElevatedThenUnelevated(), new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(1, _factoryCalls);
        var received = Assert.Single(_channel.Received);
        Assert.Equal((OperationKind.Install, ElevatedId), received);
        Assert.False(await IsInstalledAsync(ElevatedId));
        Assert.True(await IsInstalledAsync(UnelevatedId));

        var elevatedLines = progress.Events.OfType<OperationLine>().Where(e => e.Index == 0).Select(e => e.Text);
        Assert.Equal(FakeElevatedChannel.OutputLines, elevatedLines);
        var elevatedFinished = Assert.Single(progress.Events.OfType<OperationFinished>(), e => e.Index == 0);
        Assert.Equal(FakeElevatedChannel.OutputLines, elevatedFinished.Result.Log);

        Assert.True(_channel.ShutdownRequested);
        Assert.True(_channel.Disposed);
    }

    [Fact]
    public async Task RunAsync_ChannelOpens_ReportsRequestingConnectedClosedAroundOperations()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();

        await runner.RunAsync(ElevatedThenUnelevated(), new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(["requesting", "connected", "closed"], ElevationStates(progress));

        var events = progress.Events.Where(e => e is not OperationLine).ToList();
        Assert.IsType<BatchStarted>(events[0]);
        Assert.Equal(new ElevationState("requesting"), events[1]);
        Assert.Equal(new ElevationState("connected"), events[2]);
        Assert.IsType<OperationStarted>(events[3]);
        Assert.Equal(new ElevationState("closed"), events[^2]);
        Assert.IsType<BatchFinished>(events[^1]);
    }

    [Fact]
    public async Task RunAsync_UacDeclined_CancelsOnlyElevatedOperation()
    {
        var runner = CreateRunner(_ => FailWith(new ElevationDeclinedException()));
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(ElevatedThenUnelevated(), new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(["requesting", "declined"], ElevationStates(progress));
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Canceled);
        Assert.Equal(0, summary.Failed);

        var canceled = Assert.Single(progress.Events.OfType<OperationCanceled>());
        Assert.Equal(0, canceled.Index);
        Assert.Contains(new OperationLine(0, "Canceled by UAC"), progress.Events);
        Assert.DoesNotContain(progress.Events.OfType<OperationStarted>(), e => e.Index == 0);
        Assert.False(await IsInstalledAsync(ElevatedId));
        Assert.True(await IsInstalledAsync(UnelevatedId));
    }

    [Fact]
    public async Task RunAsync_HelperFailsToStart_ReportsFailureAndCancelsElevatedOperation()
    {
        var runner = CreateRunner(_ => FailWith(new TimeoutException("Elevated helper did not connect")));
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(ElevatedThenUnelevated(), new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(["requesting", "failed: Elevated helper did not connect"], ElevationStates(progress));
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Canceled);
        Assert.Equal(0, Assert.Single(progress.Events.OfType<OperationCanceled>()).Index);
        Assert.True(await IsInstalledAsync(UnelevatedId));
    }

    [Fact]
    public async Task RunAsync_ModeNever_RunsElevatedPlanInProcessAndReportsOff()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(
            ElevatedThenUnelevated(), new BatchOptions(ElevationMode: ElevationMode.Never), progress, CancellationToken.None);

        Assert.Equal(0, _factoryCalls);
        Assert.Equal(["off"], ElevationStates(progress));
        Assert.Equal(2, summary.Succeeded);
        Assert.True(await IsInstalledAsync(ElevatedId));
    }

    [Fact]
    public async Task RunAsync_ModeAlways_RoutesUnelevatedPlanThroughChannel()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(
            [Queue(UnelevatedId, elevated: false)],
            new BatchOptions(ElevationMode: ElevationMode.Always),
            progress,
            CancellationToken.None);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, _factoryCalls);
        Assert.Equal((OperationKind.Install, UnelevatedId), Assert.Single(_channel.Received));
        Assert.Equal(["requesting", "connected", "closed"], ElevationStates(progress));
        Assert.False(await IsInstalledAsync(UnelevatedId));
    }

    [Fact]
    public async Task RunAsync_ForcedPlanUnderModeNever_RoutesOnlyThatPlanThroughChannel()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();
        QueuedOperation[] operations =
        [
            Queue(ElevatedId, elevated: false, forced: true),
            Queue(UnelevatedId, elevated: true),
        ];

        var summary = await runner.RunAsync(
            operations, new BatchOptions(ElevationMode: ElevationMode.Never), progress, CancellationToken.None);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(1, _factoryCalls);
        Assert.Equal((OperationKind.Install, ElevatedId), Assert.Single(_channel.Received));
        Assert.True(await IsInstalledAsync(UnelevatedId));
    }

    [Theory]
    [InlineData(ElevationMode.Auto)]
    [InlineData(ElevationMode.Always)]
    public async Task RunAsync_ProcessElevated_RunsInProcessAndReportsRunningAsAdministrator(ElevationMode mode)
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();
        QueuedOperation[] operations = [Queue(ElevatedId, elevated: true), Queue(UnelevatedId, elevated: false, forced: true)];

        var summary = await runner.RunAsync(
            operations, new BatchOptions(ElevationMode: mode, ProcessIsElevated: true), progress, CancellationToken.None);

        Assert.Equal(0, _factoryCalls);
        Assert.Equal(["running as administrator"], ElevationStates(progress));
        Assert.Equal(2, summary.Succeeded);
        Assert.True(await IsInstalledAsync(ElevatedId));
        Assert.True(await IsInstalledAsync(UnelevatedId));
    }

    [Fact]
    public async Task RunAsync_NoElevatedOperations_NeverCallsFactoryAndReportsNotNeeded()
    {
        var runner = CreateRunner(OpenFakeChannel);
        var progress = new RecordingBatchProgress();

        await runner.RunAsync([Queue(UnelevatedId, elevated: false)], new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(0, _factoryCalls);
        Assert.Equal(["not needed"], ElevationStates(progress));
    }

    [Fact]
    public async Task RunAsync_ElevatedPlanWithNoFactory_RunsInProcessAndReportsNoState()
    {
        var runner = CreateRunner(factory: null);
        var progress = new RecordingBatchProgress();

        var summary = await runner.RunAsync(ElevatedThenUnelevated(), new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(2, summary.Succeeded);
        Assert.Empty(ElevationStates(progress));
        Assert.True(await IsInstalledAsync(ElevatedId));
    }

    /// <summary>
    /// Records each operation it is asked to run and answers with the same two lines and exit 0.
    /// </summary>
    private sealed class FakeElevatedChannel : IElevatedOperationChannel
    {
        public static readonly string[] OutputLines = ["Elevated: starting", "Elevated: done"];

        public List<(OperationKind Kind, string Id)> Received { get; } = [];

        public bool ShutdownRequested { get; private set; }

        public bool Disposed { get; private set; }

        public Task<OperationResult> RunAsync(
            OperationKind kind, OperationRequest request, IProgress<string> output, CancellationToken ct)
        {
            Received.Add((kind, request.Id));
            foreach (var line in OutputLines)
            {
                output.Report(line);
            }

            return Task.FromResult(new OperationResult(0, true, TimeSpan.FromMilliseconds(10), OutputLines));
        }

        public Task ShutdownAsync(CancellationToken ct)
        {
            ShutdownRequested = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBatchProgress : IProgress<BatchProgress>
    {
        public List<BatchProgress> Events { get; } = [];

        public void Report(BatchProgress value) => Events.Add(value);
    }
}
