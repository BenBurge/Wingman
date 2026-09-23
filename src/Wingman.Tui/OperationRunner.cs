using System.Diagnostics;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Tui;

/// <summary>The operation <see cref="OperationRunner"/> is running: what it does, and to which package.</summary>
internal sealed record RunningOperation(OperationKind Kind, string Id)
{
    /// <summary>The winget command, lowercase: <c>install</c>, <c>upgrade</c>, or <c>uninstall</c>.</summary>
    public string Verb => Kind.ToString().ToLowerInvariant();
}

/// <summary>
/// How an operation ended. <paramref name="Result"/> is null when the client threw instead of
/// returning, and <paramref name="Error"/> is what it threw; cancellation arrives as an
/// <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed record OperationOutcome(TimeSpan Elapsed, OperationResult? Result, Exception? Error)
{
    public bool Succeeded => Result is { Succeeded: true };

    public bool WasCanceled => Error is OperationCanceledException;
}

/// <summary>
/// Runs one install, upgrade, or uninstall at a time on a background task. Output lines and the
/// outcome come back through <c>invoke</c>, which runs them on the UI thread in the order they
/// were posted, so every line arrives before the outcome. Call every member on that UI thread.
/// </summary>
internal sealed class OperationRunner(IWingetClient client, Action<Action> invoke)
{
    private CancellationTokenSource? _cancellation;

    /// <summary>The operation in progress, or null when none is.</summary>
    public RunningOperation? Current { get; private set; }

    public bool IsRunning => Current is not null;

    /// <summary><c>winget upgrade --id …</c> as the client runs it, for showing above the log.</summary>
    public static string CommandLine(OperationKind kind, OperationRequest request)
    {
        var arguments = kind switch
        {
            OperationKind.Install => WingetArguments.Install(request),
            OperationKind.Upgrade => WingetArguments.Upgrade(request),
            _ => WingetArguments.Uninstall(request),
        };
        return "winget " + string.Join(' ', arguments);
    }

    /// <summary>
    /// Starts <paramref name="kind"/> on <paramref name="request"/>. Check <see cref="IsRunning"/>
    /// first: only one operation may run at a time.
    /// </summary>
    public void Start(
        OperationKind kind,
        OperationRequest request,
        Action<string> onLine,
        Action<OperationOutcome> onFinished)
    {
        if (Current is { } running)
        {
            throw new InvalidOperationException($"{running.Verb} {running.Id} is still running.");
        }

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Current = new RunningOperation(kind, request.Id);

        var output = new InvokingProgress(line => invoke(() => onLine(line)));
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            OperationOutcome outcome;
            try
            {
                var result = kind switch
                {
                    OperationKind.Install => await client.InstallAsync(request, output, token),
                    OperationKind.Upgrade => await client.UpgradeAsync(request, output, token),
                    _ => await client.UninstallAsync(request, output, token),
                };
                outcome = new OperationOutcome(stopwatch.Elapsed, result, null);
            }
            catch (Exception ex)
            {
                outcome = new OperationOutcome(stopwatch.Elapsed, null, ex);
            }

            invoke(() => Finish(cancellation, outcome, onFinished));
        });
    }

    /// <summary>Asks the running operation to stop; it still finishes through its outcome callback.</summary>
    public void Cancel() => _cancellation?.Cancel();

    private void Finish(CancellationTokenSource cancellation, OperationOutcome outcome, Action<OperationOutcome> onFinished)
    {
        cancellation.Dispose();
        _cancellation = null;
        Current = null;
        onFinished(outcome);
    }

    /// <summary>
    /// Forwards each line synchronously, so the order of <c>invoke</c> calls is the order winget
    /// printed. <see cref="Progress{T}"/> posts each report asynchronously instead, to the thread
    /// pool when there is no synchronization context, which can reorder lines or deliver one after
    /// the outcome.
    /// </summary>
    private sealed class InvokingProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
