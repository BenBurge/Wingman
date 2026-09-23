using Wingman.Core.Bundles;
using Wingman.Core.History;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class BatchRunnerTests : IDisposable
{
    // Ids chosen for how FakeWingetClient treats them: AutoHotkey is installed with an upgrade
    // available, the two search-git.txt packages are not installed, and any id containing
    // "fail" fails with 1603.
    private const string UpgradableId = "AutoHotkey.AutoHotkey";
    private const string InstallableId = "Axosoft.GitKraken";
    private const string OtherInstallableId = "GitButler.GitButler";
    private const string FailingId = "Vendor.WillFail";

    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    private readonly FakeWingetClient _client = new(TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private BatchRunner CreateRunner(FakeProcessRunner? processRunner = null) =>
        new(_client, new PrePostCommandRunner(processRunner ?? new FakeProcessRunner()), new HistoryStore(_directoryPath));

    private static QueuedOperation Queue(OperationKind kind, string id, InstallOptions? options = null)
    {
        var row = new PackageRow(Name: $"{id} name", Id: id, Version: "1.0", AvailableVersion: null, Source: "winget");
        var plan = OperationRequestFactory.Create(kind, row, new WingmanSettings(), options ?? new InstallOptions());
        return new QueuedOperation(kind, row, plan);
    }

    private static List<Type> EventTypesWithoutLines(RecordingBatchProgress progress) =>
        progress.Events.Where(e => e is not OperationLine).Select(e => e.GetType()).ToList();

    private async Task<bool> IsInstalledAsync(string id)
    {
        var installed = await _client.ListInstalledAsync(CancellationToken.None);
        return installed.Any(row => row.Id == id);
    }

    [Fact]
    public async Task RunAsync_AllSucceed_CountsReportsAndWritesHistory()
    {
        var runner = CreateRunner();
        var progress = new RecordingBatchProgress();
        QueuedOperation[] operations =
        [
            Queue(OperationKind.Upgrade, UpgradableId),
            Queue(OperationKind.Install, InstallableId),
            Queue(OperationKind.Install, OtherInstallableId),
        ];

        var summary = await runner.RunAsync(operations, new BatchOptions(), progress, CancellationToken.None);

        Assert.Equal(3, summary.Total);
        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Canceled);
        Assert.Equal(
            [
                typeof(BatchStarted),
                typeof(OperationStarted), typeof(OperationFinished),
                typeof(OperationStarted), typeof(OperationFinished),
                typeof(OperationStarted), typeof(OperationFinished),
                typeof(BatchFinished),
            ],
            EventTypesWithoutLines(progress));
        var finished = Assert.IsType<BatchFinished>(progress.Events[^1]);
        Assert.Equal(summary, finished.Summary);

        var history = new HistoryStore(_directoryPath);
        var entries = history.List();
        Assert.Equal(4, entries.Count);
        Assert.All(entries, entry => Assert.Equal(summary.BatchId, entry.BatchId));

        var operationEntries = entries.Where(e => e.Operation != "batch").ToList();
        Assert.Equal(3, operationEntries.Count);
        Assert.Contains(operationEntries, e => e.Operation == "upgrade" && e.PackageId == UpgradableId);
        Assert.Contains(operationEntries, e => e.Operation == "install" && e.PackageId == InstallableId);
        Assert.Contains(operationEntries, e => e.Operation == "install" && e.PackageId == OtherInstallableId);

        var batchEntry = Assert.Single(entries, e => e.Operation == "batch");
        Assert.Equal(summary.BatchId, batchEntry.PackageId);
        Assert.Equal("3 operations", batchEntry.PackageName);
        Assert.Equal(0, batchEntry.ExitCode);
        Assert.True(batchEntry.Succeeded);

        var batchLog = history.ReadLog(batchEntry).TrimEnd('\n').Split('\n');
        Assert.Equal(3, batchLog.Length);
        Assert.StartsWith($"✓ {UpgradableId} upgrade ", batchLog[0]);
        Assert.EndsWith(" s", batchLog[0]);
        Assert.StartsWith($"✓ {InstallableId} install ", batchLog[1]);
    }

    [Fact]
    public async Task RunAsync_FailureWithContinueOnFailure_RunsTheRest()
    {
        var runner = CreateRunner();
        var progress = new RecordingBatchProgress();
        QueuedOperation[] operations =
        [
            Queue(OperationKind.Install, InstallableId),
            Queue(OperationKind.Install, FailingId),
            Queue(OperationKind.Install, OtherInstallableId),
        ];

        var summary = await runner.RunAsync(
            operations, new BatchOptions(ContinueOnFailure: true), progress, CancellationToken.None);

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Canceled);
        Assert.True(await IsInstalledAsync(OtherInstallableId));

        var failed = Assert.Single(progress.Events.OfType<OperationFinished>(), e => !e.Result.Succeeded);
        Assert.Equal(1, failed.Index);
        Assert.Equal(1603, failed.Result.ExitCode);
        Assert.False(failed.Skipped);

        var batchEntry = Assert.Single(new HistoryStore(_directoryPath).List(), e => e.Operation == "batch");
        Assert.Equal(1, batchEntry.ExitCode);
        Assert.Contains($"✗ {FailingId} install exit 1603", new HistoryStore(_directoryPath).ReadLog(batchEntry));
    }

    [Fact]
    public async Task RunAsync_FailureWithoutContinueOnFailure_CancelsTheRest()
    {
        var runner = CreateRunner();
        var progress = new RecordingBatchProgress();
        QueuedOperation[] operations =
        [
            Queue(OperationKind.Install, InstallableId),
            Queue(OperationKind.Install, FailingId),
            Queue(OperationKind.Install, OtherInstallableId),
        ];

        var summary = await runner.RunAsync(
            operations, new BatchOptions(ContinueOnFailure: false), progress, CancellationToken.None);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Canceled);
        Assert.False(await IsInstalledAsync(OtherInstallableId));

        var canceled = Assert.Single(progress.Events.OfType<OperationCanceled>());
        Assert.Equal(2, canceled.Index);
        Assert.DoesNotContain(progress.Events.OfType<OperationStarted>(), e => e.Index == 2);

        var entries = new HistoryStore(_directoryPath).List();
        Assert.Equal(3, entries.Count);
        var batchEntry = Assert.Single(entries, e => e.Operation == "batch");
        Assert.Equal(1, batchEntry.ExitCode);
        Assert.False(batchEntry.Succeeded);
    }

    [Fact]
    public async Task RunAsync_PreCommandFailsWithAbort_SkipsWithoutCallingClient()
    {
        var processRunner = new FakeProcessRunner { ExitCode = 7 };
        var runner = CreateRunner(processRunner);
        var progress = new RecordingBatchProgress();
        var options = new InstallOptions { PreInstallCommand = "exit 7", AbortOnPreInstallFail = true };
        QueuedOperation[] operations = [Queue(OperationKind.Install, InstallableId, options)];
        var installedBefore = await _client.ListInstalledAsync(CancellationToken.None);

        var summary = await runner.RunAsync(operations, new BatchOptions(), progress, CancellationToken.None);

        var finished = Assert.Single(progress.Events.OfType<OperationFinished>());
        Assert.True(finished.Skipped);
        Assert.Equal(7, finished.Result.ExitCode);
        Assert.False(finished.Result.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Succeeded);
        Assert.Single(processRunner.Calls);

        var installedAfter = await _client.ListInstalledAsync(CancellationToken.None);
        Assert.Equal(installedBefore, installedAfter);

        var history = new HistoryStore(_directoryPath);
        var batchEntry = Assert.Single(history.List(), e => e.Operation == "batch");
        Assert.Contains($"· {InstallableId} skipped", history.ReadLog(batchEntry));
    }

    [Fact]
    public async Task RunAsync_CanceledAfterFirstOperation_CancelsTheRestWithoutThrowing()
    {
        var runner = CreateRunner();
        using var cts = new CancellationTokenSource();
        var progress = new RecordingBatchProgress
        {
            OnReport = e =>
            {
                if (e is OperationFinished { Index: 0 })
                {
                    cts.Cancel();
                }
            },
        };
        QueuedOperation[] operations =
        [
            Queue(OperationKind.Install, InstallableId),
            Queue(OperationKind.Install, OtherInstallableId),
            Queue(OperationKind.Upgrade, UpgradableId),
        ];

        var summary = await runner.RunAsync(operations, new BatchOptions(), progress, cts.Token);

        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(2, summary.Canceled);
        Assert.Equal([1, 2], progress.Events.OfType<OperationCanceled>().Select(e => e.Index));
        Assert.IsType<BatchFinished>(progress.Events[^1]);
        Assert.False(await IsInstalledAsync(OtherInstallableId));
    }

    [Fact]
    public async Task RunAsync_SuccessfulInstall_ForwardsStreamedLinesInOrder()
    {
        var runner = CreateRunner();
        var progress = new RecordingBatchProgress();
        var operation = Queue(OperationKind.Install, InstallableId);
        var expected = new RecordingProgress();
        await new FakeWingetClient(TimeSpan.Zero).InstallAsync(operation.Plan.Request, expected, CancellationToken.None);

        await runner.RunAsync([operation], new BatchOptions(), progress, CancellationToken.None);

        var lines = progress.Events.OfType<OperationLine>().ToList();
        Assert.All(lines, line => Assert.Equal(0, line.Index));
        Assert.Equal(expected.Lines, lines.Select(line => line.Text));

        var finished = Assert.Single(progress.Events.OfType<OperationFinished>());
        Assert.Equal(expected.Lines, finished.Result.Log);
    }

    /// <summary>
    /// Records synchronously, like <see cref="RecordingProgress"/>, and lets a test react to an
    /// event while the batch is still running.
    /// </summary>
    private sealed class RecordingBatchProgress : IProgress<BatchProgress>
    {
        public List<BatchProgress> Events { get; } = [];

        public Action<BatchProgress>? OnReport { get; init; }

        public void Report(BatchProgress value)
        {
            Events.Add(value);
            OnReport?.Invoke(value);
        }
    }
}
