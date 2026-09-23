using System.Globalization;
using Wingman.Core.History;

namespace Wingman.Tui;

internal enum HistoryResult
{
    Ok,
    Failed,
    Canceled,
}

/// <summary>One line of the History tab: a stored <see cref="HistoryEntry"/> and how it ended.</summary>
internal sealed record HistoryRow(HistoryEntry Entry, HistoryResult Result)
{
    public const string BatchOperation = "batch";

    // BatchRunner's last log line for an operation it canceled mid-run, and the end of a batch's
    // summary line for one it canceled before starting.
    private const string CanceledLogLine = "Canceled";
    private const string CanceledSummarySuffix = " canceled";

    public bool IsBatch => Entry.Operation == BatchOperation;

    /// <summary><c>2026-09-22 14:31</c> in local time.</summary>
    public string When => Entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The package Id, or for a batch entry its <c>3 operations</c> name, since its Id is only the batch's.</summary>
    public string Package => IsBatch ? Entry.PackageName : Entry.PackageId;

    public string ResultText => Result switch
    {
        HistoryResult.Ok => "ok",
        HistoryResult.Canceled => "canceled",
        _ => "failed",
    };

    /// <summary><c>21 s</c>, or minutes once it would need four digits; empty for a batch entry, whose operations carry their own.</summary>
    public string DurationText
    {
        get
        {
            if (IsBatch)
            {
                return "";
            }

            var seconds = (int)Math.Round(Entry.Duration.TotalSeconds);
            return seconds < 1000 ? $"{seconds} s" : $"{seconds / 60} m";
        }
    }

    /// <summary>
    /// Pairs each entry with how it ended. <see cref="HistoryEntry"/> has no canceled flag, so the
    /// logs decide: an operation canceled mid-run ends its log with <c>Canceled</c>, and a batch was
    /// canceled when it canceled something, mid-run or before it started, and nothing in it failed.
    /// Reads the log of every failed entry, so call it off the UI thread.
    /// </summary>
    public static List<HistoryRow> FromEntries(HistoryStore store, IReadOnlyList<HistoryEntry> entries)
    {
        // Every operation first, so each batch can look at how its operations ended.
        var results = new HistoryResult[entries.Count];
        var resultsByBatch = new Dictionary<string, List<HistoryResult>>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Operation == BatchOperation)
            {
                continue;
            }

            results[i] = OperationResult(store, entry);
            if (entry.BatchId is { } batchId)
            {
                if (!resultsByBatch.TryGetValue(batchId, out var batchResults))
                {
                    batchResults = [];
                    resultsByBatch[batchId] = batchResults;
                }

                batchResults.Add(results[i]);
            }
        }

        var rows = new List<HistoryRow>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Operation == BatchOperation)
            {
                var operationResults = resultsByBatch.GetValueOrDefault(entry.BatchId ?? "") ?? [];
                results[i] = BatchResult(store, entry, operationResults);
            }

            rows.Add(new HistoryRow(entry, results[i]));
        }

        return rows;
    }

    private static HistoryResult OperationResult(HistoryStore store, HistoryEntry entry)
    {
        if (entry.Succeeded)
        {
            return HistoryResult.Ok;
        }

        var lines = LogLines(store, entry);
        return lines.Length > 0 && lines[^1] == CanceledLogLine ? HistoryResult.Canceled : HistoryResult.Failed;
    }

    /// <param name="operationResults">How each of the batch's operations still in the history ended.</param>
    private static HistoryResult BatchResult(HistoryStore store, HistoryEntry batch, List<HistoryResult> operationResults)
    {
        if (batch.Succeeded)
        {
            return HistoryResult.Ok;
        }

        if (operationResults.Contains(HistoryResult.Failed))
        {
            return HistoryResult.Failed;
        }

        var canceledBeforeStarting = LogLines(store, batch).Any(line => line.EndsWith(CanceledSummarySuffix, StringComparison.Ordinal));
        var isCanceled = canceledBeforeStarting || operationResults.Contains(HistoryResult.Canceled);
        return isCanceled ? HistoryResult.Canceled : HistoryResult.Failed;
    }

    private static string[] LogLines(HistoryStore store, HistoryEntry entry)
    {
        try
        {
            return store.ReadLog(entry).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (IOException)
        {
            return [];
        }
    }
}
