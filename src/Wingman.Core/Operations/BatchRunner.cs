using System.Diagnostics;
using System.Globalization;
using Wingman.Core.Elevation;
using Wingman.Core.History;
using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Operations;

/// <summary>
/// Runs a queue of operations one after another, with each operation's pre- and post-commands,
/// writing one history entry per operation and a closing <c>batch</c> entry. Reports every step
/// to the caller's <see cref="IProgress{T}"/> and leaves marshaling to a UI thread to the caller.
/// </summary>
/// <remarks>
/// Operations whose plan requires elevation run through one elevated channel opened for the whole
/// batch, when a channel factory is given and <see cref="BatchOptions.AutoElevate"/> is on. Their
/// pre- and post-commands still run in this process.
/// </remarks>
public sealed class BatchRunner
{
    private const int FailedExitCode = -1;

    private readonly IWingetClient _client;
    private readonly PrePostCommandRunner _commands;
    private readonly HistoryStore _history;
    private readonly Func<CancellationToken, Task<IElevatedOperationChannel>>? _elevatedChannelFactory;

    /// <param name="elevatedChannelFactory">Opens the elevated helper, prompting for UAC; null runs
    /// every operation in-process, as on non-Windows systems and with the fake client.</param>
    public BatchRunner(
        IWingetClient client,
        PrePostCommandRunner commands,
        HistoryStore history,
        Func<CancellationToken, Task<IElevatedOperationChannel>>? elevatedChannelFactory = null)
    {
        _client = client;
        _commands = commands;
        _history = history;
        _elevatedChannelFactory = elevatedChannelFactory;
    }

    /// <summary>
    /// Runs <paramref name="operations"/> in order. Never throws for a failed, faulted, or canceled
    /// operation: those are recorded in history and counted in the returned summary.
    /// </summary>
    public async Task<BatchSummary> RunAsync(
        IReadOnlyList<QueuedOperation> operations,
        BatchOptions options,
        IProgress<BatchProgress> progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var batchId = NewBatchId();
        progress.Report(new BatchStarted(batchId, operations.Count));

        var summaryLines = new List<string>();
        var succeeded = 0;
        var failed = 0;
        var canceled = 0;
        var stopping = false;

        var elevation = await OpenElevatedChannelAsync(operations, options, progress, ct);

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            var elevationUnavailableReason = operation.Plan.RequiresElevation ? elevation.UnavailableReason : null;
            if (stopping || ct.IsCancellationRequested || elevationUnavailableReason is not null)
            {
                if (elevationUnavailableReason is not null)
                {
                    progress.Report(new OperationLine(index, elevationUnavailableReason));
                }

                progress.Report(new OperationCanceled(index, operation));
                summaryLines.Add($"· {operation.Row.Id} canceled");
                canceled++;
                continue;
            }

            var outcome = await RunOperationAsync(index, operation, elevation.Channel, progress, ct);
            progress.Report(new OperationFinished(index, operation, outcome.Result, outcome.Skipped));

            var plan = operation.Plan;
            _history.Append(
                DateTimeOffset.Now,
                Verb(plan.Kind),
                operation.Row.Id,
                operation.Row.Name,
                outcome.Result,
                Arguments(plan),
                batchId);
            summaryLines.Add(SummaryLine(operation, outcome));

            if (outcome.Result.Succeeded)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }

            var abortAfterFailure = !outcome.Result.Succeeded && !options.ContinueOnFailure;
            stopping = outcome.WasCanceled || abortAfterFailure;
        }

        if (elevation.Channel is not null)
        {
            await CloseElevatedChannelAsync(elevation.Channel);
            progress.Report(new ElevationState("closed"));
        }

        var duration = stopwatch.Elapsed;
        var summary = new BatchSummary(batchId, operations.Count, succeeded, failed, canceled, duration);

        var batchExitCode = failed == 0 && canceled == 0 ? 0 : 1;
        var batchResult = new OperationResult(batchExitCode, batchExitCode == 0, duration, summaryLines);
        _history.Append(
            DateTimeOffset.Now, "batch", batchId, $"{operations.Count} operations", batchResult, [], batchId);

        progress.Report(new BatchFinished(summary));
        return summary;
    }

    /// <summary>
    /// Opens the elevated channel when this batch needs one. A declined UAC prompt or a helper
    /// that fails to start does not end the batch: the result carries the line to show on each
    /// elevated operation, which is then canceled while the others still run.
    /// </summary>
    private async Task<ElevationOutcome> OpenElevatedChannelAsync(
        IReadOnlyList<QueuedOperation> operations,
        BatchOptions options,
        IProgress<BatchProgress> progress,
        CancellationToken ct)
    {
        var needsHelper = ElevationPolicy.NeedsHelper(operations, options.AutoElevate);
        if (_elevatedChannelFactory is null || !needsHelper)
        {
            return new ElevationOutcome(null, null);
        }

        progress.Report(new ElevationState("requesting"));
        try
        {
            var channel = await _elevatedChannelFactory(ct);
            progress.Report(new ElevationState("connected"));
            return new ElevationOutcome(channel, null);
        }
        catch (ElevationDeclinedException ex)
        {
            progress.Report(new ElevationState("declined"));
            return new ElevationOutcome(null, ex.Message);
        }
        catch (Exception ex)
        {
            progress.Report(new ElevationState($"failed: {ex.Message}"));
            return new ElevationOutcome(null, $"Canceled: elevated helper failed: {ex.Message}");
        }
    }

    // Not canceled by the batch token: a canceled batch still has to tell the helper to exit.
    // A helper that already disconnected needs no shutdown message.
    private static async Task CloseElevatedChannelAsync(IElevatedOperationChannel channel)
    {
        try
        {
            await channel.ShutdownAsync(CancellationToken.None);
        }
        catch (IOException)
        {
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    private async Task<OperationOutcome> RunOperationAsync(
        int index,
        QueuedOperation operation,
        IElevatedOperationChannel? elevatedChannel,
        IProgress<BatchProgress> progress,
        CancellationToken ct)
    {
        progress.Report(new OperationStarted(index, operation));

        var plan = operation.Plan;
        var lines = new LineCollector(index, progress);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var preExitCode = await _commands.RunPreAsync(plan, lines, ct);
            if (preExitCode is int exitCode && PrePostCommandRunner.ShouldSkipOperation(plan, exitCode))
            {
                var skipped = PrePostCommandRunner.SkippedResult(exitCode, lines.Snapshot());
                return new OperationOutcome(skipped, Skipped: true, WasCanceled: false);
            }

            var result = await RunWingetAsync(plan, elevatedChannel, lines, ct);
            await _commands.RunPostAsync(plan, lines, ct);

            // The client's log holds only winget's lines; history keeps the pre- and
            // post-command output around them too.
            return new OperationOutcome(result with { Log = lines.Snapshot() }, Skipped: false, WasCanceled: false);
        }
        catch (OperationCanceledException)
        {
            lines.Report("Canceled");
            var canceled = new OperationResult(FailedExitCode, false, stopwatch.Elapsed, lines.Snapshot());
            return new OperationOutcome(canceled, Skipped: false, WasCanceled: true);
        }
        catch (Exception ex)
        {
            lines.Report($"Failed: {ex.Message}");
            var faulted = new OperationResult(FailedExitCode, false, stopwatch.Elapsed, lines.Snapshot());
            return new OperationOutcome(faulted, Skipped: false, WasCanceled: false);
        }
    }

    private Task<OperationResult> RunWingetAsync(
        OperationPlan plan, IElevatedOperationChannel? elevatedChannel, IProgress<string> output, CancellationToken ct)
    {
        if (plan.RequiresElevation && elevatedChannel is not null)
        {
            return elevatedChannel.RunAsync(plan.Kind, plan.Request, output, ct);
        }

        return plan.Kind switch
        {
            OperationKind.Install => _client.InstallAsync(plan.Request, output, ct),
            OperationKind.Upgrade => _client.UpgradeAsync(plan.Request, output, ct),
            OperationKind.Uninstall => _client.UninstallAsync(plan.Request, output, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Kind, null),
        };
    }

    private static string[] Arguments(OperationPlan plan) => plan.Kind switch
    {
        OperationKind.Install => WingetArguments.Install(plan.Request),
        OperationKind.Upgrade => WingetArguments.Upgrade(plan.Request),
        OperationKind.Uninstall => WingetArguments.Uninstall(plan.Request),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Kind, null),
    };

    private static string Verb(OperationKind kind) => kind switch
    {
        OperationKind.Install => "install",
        OperationKind.Upgrade => "upgrade",
        OperationKind.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string SummaryLine(QueuedOperation operation, OperationOutcome outcome)
    {
        var id = operation.Row.Id;
        if (outcome.Skipped)
        {
            return $"· {id} skipped";
        }

        var verb = Verb(operation.Plan.Kind);
        var result = outcome.Result;
        if (result.Succeeded)
        {
            var seconds = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            return $"✓ {id} {verb} {seconds} s";
        }

        return $"✗ {id} {verb} exit {result.ExitCode}";
    }

    // The timestamp alone collides when two batches start in the same second.
    private static string NewBatchId()
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
        var suffix = Random.Shared.Next(0x10000).ToString("x4", CultureInfo.InvariantCulture);
        return $"{timestamp}-{suffix}";
    }

    private readonly record struct OperationOutcome(OperationResult Result, bool Skipped, bool WasCanceled);

    /// <param name="Channel">The open elevated channel, or null when the batch runs in-process.</param>
    /// <param name="UnavailableReason">Set when elevation was needed but could not be had; the
    /// line reported on each elevated operation before it is canceled.</param>
    private readonly record struct ElevationOutcome(IElevatedOperationChannel? Channel, string? UnavailableReason);

    /// <summary>
    /// Forwards each line as an <see cref="OperationLine"/> and keeps it for the history log.
    /// Locked because <see cref="ProcessRunner"/> reports stdout and stderr from separate threads.
    /// </summary>
    private sealed class LineCollector : IProgress<string>
    {
        private readonly int _index;
        private readonly IProgress<BatchProgress> _progress;
        private readonly Lock _lock = new();
        private readonly List<string> _lines = [];

        public LineCollector(int index, IProgress<BatchProgress> progress)
        {
            _index = index;
            _progress = progress;
        }

        public void Report(string value)
        {
            lock (_lock)
            {
                _lines.Add(value);
                _progress.Report(new OperationLine(_index, value));
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }
}
