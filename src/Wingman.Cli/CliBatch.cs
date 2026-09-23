using Wingman.Core.Elevation;
using Wingman.Core.Notifications;
using Wingman.Core.Operations;
using Wingman.Core.State;
using Wingman.Core.Winget;

namespace Wingman.Cli;

/// <summary>The result of running a plan: the runner's summary and how each operation ended.</summary>
internal sealed record BatchRun(BatchSummary Summary, IReadOnlyDictionary<int, OperationOutcome> Outcomes);

/// <summary>
/// The path <c>upgrade</c>, <c>install</c>, and <c>import</c> share once they have a plan: print it,
/// confirm it, run it through <see cref="BatchRunner"/> with progress on the console, keep
/// <c>state.json</c> current, and report the outcome as text or JSON.
/// </summary>
internal static class CliBatch
{
    public const string RefuseText = "Refusing to run without --yes when stdin is not a terminal.";

    /// <summary>
    /// Where plan and progress text goes: standard error under <c>--json</c>, so standard output
    /// holds nothing but the JSON document.
    /// </summary>
    public static TextWriter TextOut(CliContext context) => context.Json ? context.Error : context.Out;

    /// <summary>Prints the plan, confirms it, runs it, and returns the exit code; the whole of <c>upgrade</c> and <c>install</c>.</summary>
    public static async Task<int> RunPlanAsync(CliContext context, CliArgs args, Plan plan)
    {
        var dryRun = args.HasFlag("dry-run");
        WritePlan(context, plan);

        if (plan.Operations.Count == 0)
        {
            TextOut(context).WriteLine("Nothing to do.");
            WriteJson(context, plan, run: null, dryRun);
            return ExitCodes.Success;
        }

        if (dryRun)
        {
            WriteJson(context, plan, run: null, dryRun);
            return ExitCodes.Success;
        }

        var refusal = Confirm(context, args);
        if (refusal is int exitCode)
        {
            return exitCode;
        }

        var run = await RunAsync(context, plan.Operations);
        WriteJson(context, plan, run, dryRun);
        return ExitCode(context, run.Summary);
    }

    /// <summary>One line per operation, <c>⚡</c> on those that go through the elevated helper, then the notes.</summary>
    public static void WritePlan(CliContext context, Plan plan)
    {
        var writer = TextOut(context);
        foreach (var operation in plan.Operations)
        {
            var line = PlanBuilder.Describe(operation);
            writer.WriteLine(UsesHelper(context, operation) ? $"{line} ⚡" : line);
        }

        foreach (var note in plan.Notes)
        {
            writer.WriteLine(note);
        }
    }

    /// <summary>
    /// Asks <c>Proceed? [y/N]</c> unless <c>--yes</c> was given. Returns null to go ahead, or the
    /// exit code to stop with: <see cref="ExitCodes.Usage"/> when standard input cannot answer, and
    /// <see cref="ExitCodes.Canceled"/> when the answer is anything but yes.
    /// </summary>
    public static int? Confirm(CliContext context, CliArgs args)
    {
        if (args.HasFlag("yes"))
        {
            return null;
        }

        if (context.IsInputRedirected)
        {
            context.Error.WriteLine(RefuseText);
            return ExitCodes.Usage;
        }

        var writer = TextOut(context);
        writer.Write("Proceed? [y/N] ");
        writer.Flush();

        var answer = context.Input.ReadLine()?.Trim() ?? "";
        var confirmed = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
        if (confirmed)
        {
            return null;
        }

        writer.WriteLine("Canceled.");
        return ExitCodes.Canceled;
    }

    /// <summary>
    /// Runs <paramref name="operations"/> with the settings' failure and elevation options, marking
    /// <c>state.json</c> as running meanwhile and recording the batch's result in it afterward.
    /// </summary>
    public static async Task<BatchRun> RunAsync(CliContext context, IReadOnlyList<QueuedOperation> operations)
    {
        var settings = context.Settings;
        var reporter = new BatchConsoleReporter(TextOut(context), context.UseColor);
        var runner = new BatchRunner(
            context.Client,
            new PrePostCommandRunner(new ProcessRunner()),
            context.History,
            context.ElevationFactory);
        var options = new BatchOptions(settings.ContinueOnFailure, settings.ElevationMode, context.ProcessIsElevated);

        context.State.Update(state => state.Running = true);
        BatchSummary? summary = null;
        try
        {
            summary = await runner.RunAsync(operations, options, reporter, context.Cancel);
        }
        finally
        {
            context.State.Update(state =>
            {
                state.Running = false;
                if (summary is not null)
                {
                    RecordBatch(state, summary, operations, reporter.Outcomes);
                }
            });
        }

        var wantsToast = context.Notify && settings.ToastOnBatch && !settings.NotificationsPaused;
        if (wantsToast && context.ToastSender is { } toasts)
        {
            var toast = ToastBuilder.BatchFinished(summary.Total, summary.Succeeded, summary.Failed, summary.Canceled);
            // A batch stopped by Ctrl+C still reports itself; the sender has its own timeout.
            await toasts.SendAsync(toast, CancellationToken.None);
        }

        return new BatchRun(summary, reporter.Outcomes);
    }

    /// <summary><see cref="ExitCodes.Canceled"/> after Ctrl+C, success when every operation succeeded, failure otherwise.</summary>
    public static int ExitCode(CliContext context, BatchSummary summary)
    {
        if (context.Cancel.IsCancellationRequested)
        {
            return ExitCodes.Canceled;
        }

        var allSucceeded = summary.Failed == 0 && summary.Canceled == 0;
        return allSucceeded ? ExitCodes.Success : ExitCodes.Failure;
    }

    /// <summary>
    /// The plan, and the run's outcome when there was one, as the one document <c>--json</c> prints;
    /// does nothing without <c>--json</c>.
    /// </summary>
    public static void WriteJson(CliContext context, Plan plan, BatchRun? run, bool dryRun)
    {
        if (!context.Json)
        {
            return;
        }

        var operations = new List<object>(plan.Operations.Count);
        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];
            var (from, to) = PlanBuilder.Versions(operation);
            OperationOutcome? outcome = null;
            run?.Outcomes.TryGetValue(index, out outcome);

            operations.Add(new
            {
                operation = operation.Plan.Label,
                id = operation.Row.Id,
                name = operation.Row.Name,
                from,
                to,
                elevated = UsesHelper(context, operation),
                result = outcome?.Kind,
                exitCode = outcome?.Result?.ExitCode,
                durationSeconds = outcome?.Result?.Duration.TotalSeconds,
            });
        }

        object? summary = null;
        if (run is not null)
        {
            summary = new
            {
                batchId = run.Summary.BatchId,
                total = run.Summary.Total,
                succeeded = run.Summary.Succeeded,
                failed = run.Summary.Failed,
                canceled = run.Summary.Canceled,
                durationSeconds = run.Summary.Duration.TotalSeconds,
            };
        }

        JsonOutput.Write(context.Out, new { dryRun, operations, notes = plan.Notes, summary });
    }

    private static bool UsesHelper(CliContext context, QueuedOperation operation) =>
        ElevationPolicy.UsesHelper(operation.Plan, context.Settings.ElevationMode, context.ProcessIsElevated);

    /// <summary>
    /// Stores the batch's result for the tray, and drops the packages it upgraded from the last
    /// check's list so the tray's count stays right until the next check.
    /// </summary>
    private static void RecordBatch(
        WingmanState state,
        BatchSummary summary,
        IReadOnlyList<QueuedOperation> operations,
        IReadOnlyDictionary<int, OperationOutcome> outcomes)
    {
        state.LastBatch = DateTimeOffset.Now;
        state.LastBatchResult = BatchConsoleReporter.SummaryLine(summary, withDuration: false);
        state.LastBatchFailed = summary.Failed > 0 || summary.Canceled > 0;

        foreach (var (index, outcome) in outcomes)
        {
            var operation = operations[index];
            var upgraded = outcome.Kind == OperationOutcomeKind.Succeeded && operation.Kind == OperationKind.Upgrade;
            if (upgraded)
            {
                state.UpdateIds.RemoveAll(id => string.Equals(id, operation.Row.Id, StringComparison.OrdinalIgnoreCase));
            }
        }

        state.UpdatesAvailable = state.UpdateIds.Count;
    }
}
