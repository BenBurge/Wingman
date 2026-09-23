using System.Globalization;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Winget;

namespace Wingman.Cli;

/// <summary>
/// Prints a <see cref="BatchRunner"/> run as it happens: a <c>▶</c> line when an operation starts,
/// its output indented under it, a <c>✓</c>, <c>✗</c>, or <c>○</c> line when it ends, the elevated
/// helper's steps, and a closing summary. Keeps each operation's outcome for the JSON summary.
/// </summary>
internal sealed class BatchConsoleReporter : IProgress<BatchProgress>
{
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Cyan = "\u001b[36m";
    private const string Reset = "\u001b[0m";

    // BatchRunner's last log line for an operation canceled while it ran.
    private const string CanceledLogLine = "Canceled";

    private readonly TextWriter _writer;
    private readonly bool _useColor;

    // Log lines arrive from winget's stdout and stderr threads and the elevation ticker runs
    // beside the prompt, so writes are serialized to keep lines whole.
    private readonly Lock _lock = new();
    private readonly Dictionary<int, OperationOutcome> _outcomes = [];

    public BatchConsoleReporter(TextWriter writer, bool useColor)
    {
        _writer = writer;
        _useColor = useColor;
    }

    /// <summary>How each operation ended, by its index in the batch; operations still running are absent.</summary>
    public IReadOnlyDictionary<int, OperationOutcome> Outcomes => _outcomes;

    public void Report(BatchProgress value)
    {
        lock (_lock)
        {
            switch (value)
            {
                case OperationStarted started:
                    WriteGlyphLine("▶", Cyan, PlanBuilder.Describe(started.Operation));
                    break;
                case OperationLine line:
                    _writer.WriteLine($"  {line.Text}");
                    break;
                case OperationFinished finished:
                    WriteFinished(finished);
                    break;
                case OperationCanceled canceled:
                    _outcomes[canceled.Index] = new OperationOutcome(OperationOutcomeKind.Canceled, null);
                    WriteGlyphLine("○", Yellow, $"{canceled.Operation.Row.Id} canceled");
                    break;
                case ElevationState state when state.State != "not needed":
                    _writer.WriteLine($"elevated helper: {state.State}");
                    break;
                case BatchFinished batchFinished:
                    _writer.WriteLine(SummaryLine(batchFinished.Summary, withDuration: true));
                    break;
            }
        }
    }

    /// <summary>
    /// <c>3 of 3 succeeded</c>, with <c>, 1 failed</c> and <c>, 1 canceled</c> when there were any,
    /// and <c> in 41 s</c> when <paramref name="withDuration"/>.
    /// </summary>
    public static string SummaryLine(BatchSummary summary, bool withDuration)
    {
        var line = $"{summary.Succeeded} of {summary.Total} succeeded";
        if (summary.Failed > 0)
        {
            line += $", {summary.Failed} failed";
        }

        if (summary.Canceled > 0)
        {
            line += $", {summary.Canceled} canceled";
        }

        return withDuration ? $"{line} in {Seconds(summary.Duration)}" : line;
    }

    private void WriteFinished(OperationFinished finished)
    {
        var id = finished.Operation.Row.Id;
        var result = finished.Result;

        if (result.Succeeded)
        {
            _outcomes[finished.Index] = new OperationOutcome(OperationOutcomeKind.Succeeded, result);
            var seconds = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            WriteGlyphLine("✓", Green, $"{id} done in {seconds} s");
            return;
        }

        if (IsCanceledWhileRunning(result))
        {
            _outcomes[finished.Index] = new OperationOutcome(OperationOutcomeKind.Canceled, result);
            WriteGlyphLine("○", Yellow, $"{id} canceled");
            return;
        }

        _outcomes[finished.Index] = new OperationOutcome(OperationOutcomeKind.Failed, result);
        var code = WingetErrorCodes.Format(result.ExitCode);
        if (finished.Skipped)
        {
            WriteGlyphLine("✗", Red, $"{id} skipped: its pre-command exited {code}");
            return;
        }

        var wingetSaid = WingetErrorCodes.Explain(result.ExitCode).WingetSaid;
        var text = wingetSaid.Length > 0 ? $"{id} failed with exit {code}: {wingetSaid}" : $"{id} failed with exit {code}";
        WriteGlyphLine("✗", Red, text);
    }

    private static bool IsCanceledWhileRunning(OperationResult result) =>
        result.Log.Count > 0 && result.Log[^1] == CanceledLogLine;

    private void WriteGlyphLine(string glyph, string color, string text)
    {
        var shownGlyph = _useColor ? $"{color}{glyph}{Reset}" : glyph;
        _writer.WriteLine($"{shownGlyph} {text}");
    }

    /// <summary><c>41 s</c> under a minute, <c>2 m 5 s</c> from there.</summary>
    private static string Seconds(TimeSpan duration)
    {
        var seconds = (int)Math.Round(duration.TotalSeconds);
        return seconds < 60 ? $"{seconds} s" : $"{seconds / 60} m {seconds % 60} s";
    }
}

internal enum OperationOutcomeKind
{
    Succeeded,
    Failed,
    Canceled,
}

/// <param name="Result">What winget returned; null for an operation canceled before it started.</param>
internal sealed record OperationOutcome(OperationOutcomeKind Kind, OperationResult? Result);
