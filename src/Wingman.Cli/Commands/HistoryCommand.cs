using System.Globalization;
using Wingman.Core.History;
using Wingman.Core.Winget;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman history</c>: past operations newest first, one operation's log, or forgetting one.
/// Entries are numbered from 1 for the newest; the <c>batch</c> entries that close each batch are
/// left out, since every operation in the batch has its own.
/// </summary>
internal sealed class HistoryCommand : ICliCommand
{
    private const int DefaultCount = 20;
    private const string BatchOperation = "batch";

    // BatchRunner's last log line for an operation canceled while it ran.
    private const string CanceledLogLine = "Canceled";

    public string Name => "history";

    public string Summary => "Show past operations and their logs";

    public string Usage => """
        Usage: wingman history [--last <n>] [--failed] [--json]
               wingman history show <n> [--json]
               wingman history forget <n> [--yes]

        Lists past operations newest first, numbered from 1. 'show' prints one operation's details
        and full log; 'forget' deletes it from the history.

          --last <n>  List the newest n operations (default 20)
          --failed    List only operations that did not succeed
          --yes       Forget without asking; required when stdin is not a terminal
          --json      Print the operations as one JSON document
        """;

    public IReadOnlyCollection<string> Flags => ["failed", "yes"];

    public Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var subcommand = args.Positionals.Count > 0 ? args.Positionals[0] : null;
        var exitCode = subcommand switch
        {
            null => List(args, context),
            "show" => Show(args, context),
            "forget" => Forget(args, context),
            _ => UsageError(context, $"unknown subcommand '{subcommand}'"),
        };
        return Task.FromResult(exitCode);
    }

    private int List(CliArgs args, CliContext context)
    {
        var count = DefaultCount;
        var last = args.GetOption("last");
        if (last is not null && (!int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1))
        {
            return UsageError(context, "--last needs a positive number");
        }

        var failedOnly = args.HasFlag("failed");
        var entries = Operations(context.History);
        var shown = new List<(int Number, HistoryEntry Entry)>();
        for (var i = 0; i < entries.Count && shown.Count < count; i++)
        {
            if (!failedOnly || !entries[i].Succeeded)
            {
                shown.Add((i + 1, entries[i]));
            }
        }

        if (context.Json)
        {
            var items = new List<object>(shown.Count);
            foreach (var (number, entry) in shown)
            {
                items.Add(ToJson(number, entry, ResultOf(context.History, entry), log: null));
            }

            JsonOutput.Write(context.Out, new { entries = items });
            return ExitCodes.Success;
        }

        if (shown.Count == 0)
        {
            context.Out.WriteLine(failedOnly ? "No failed operations." : "No operations yet.");
            return ExitCodes.Success;
        }

        var table = new TableWriter("#", "When", "Operation", "Package", "Result", "Duration");
        foreach (var (number, entry) in shown)
        {
            table.AddRow(
                number.ToString(CultureInfo.InvariantCulture),
                When(entry),
                entry.Operation,
                entry.PackageId,
                ResultOf(context.History, entry),
                Seconds(entry.Duration));
        }

        table.Write(context.Out, context.Width);
        return ExitCodes.Success;
    }

    private int Show(CliArgs args, CliContext context)
    {
        var entries = Operations(context.History);
        if (!TryPick(args, context, entries, out var number, out var exitCode))
        {
            return exitCode;
        }

        var entry = entries[number - 1];
        var log = context.History.ReadLog(entry);
        var result = ResultOf(context.History, entry);

        if (context.Json)
        {
            JsonOutput.Write(context.Out, ToJson(number, entry, result, log));
            return ExitCodes.Success;
        }

        var output = context.Out;
        output.WriteLine($"{entry.Operation} {entry.PackageId} ({entry.PackageName})");
        output.WriteLine($"When:      {entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        output.WriteLine($"Result:    {result}, exit {WingetErrorCodes.Format(entry.ExitCode)}");
        output.WriteLine($"Duration:  {Seconds(entry.Duration)}");
        if (entry.Arguments.Count > 0)
        {
            output.WriteLine($"Command:   winget {string.Join(' ', entry.Arguments)}");
        }

        if (entry.BatchId is { } batchId)
        {
            output.WriteLine($"Batch:     {batchId}");
        }

        // The store joins log lines with \n; writing them one by one gives the platform's line endings.
        output.WriteLine();
        foreach (var line in log.TrimEnd('\n').Split('\n'))
        {
            output.WriteLine(line);
        }

        return ExitCodes.Success;
    }

    private int Forget(CliArgs args, CliContext context)
    {
        var entries = Operations(context.History);
        if (!TryPick(args, context, entries, out var number, out var exitCode))
        {
            return exitCode;
        }

        var entry = entries[number - 1];
        var description = $"{entry.Operation} {entry.PackageId} from {When(entry)}";
        if (!args.HasFlag("yes"))
        {
            CliBatch.TextOut(context).WriteLine($"Forget {description}?");
        }

        var refusal = CliBatch.Confirm(context, args);
        if (refusal is int refusedCode)
        {
            return refusedCode;
        }

        context.History.Delete(entry);
        context.Out.WriteLine($"Forgot {description}.");
        return ExitCodes.Success;
    }

    /// <summary>Reads the <c>&lt;n&gt;</c> after <c>show</c> or <c>forget</c>, reporting a usage error when it names no entry.</summary>
    private bool TryPick(CliArgs args, CliContext context, IReadOnlyList<HistoryEntry> entries, out int number, out int exitCode)
    {
        number = 0;
        exitCode = ExitCodes.Success;
        var subcommand = args.Positionals[0];
        if (args.Positionals.Count < 2)
        {
            exitCode = UsageError(context, $"'{subcommand}' needs the number of an entry");
            return false;
        }

        var text = args.Positionals[1];
        var isNumber = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        if (!isNumber || number < 1 || number > entries.Count)
        {
            var noun = entries.Count == 1 ? "entry" : "entries";
            exitCode = UsageError(context, $"no entry {text}; the history has {entries.Count} {noun}");
            return false;
        }

        return true;
    }

    private int UsageError(CliContext context, string problem)
    {
        context.Error.WriteLine($"wingman history: {problem}");
        context.Error.WriteLine();
        context.Error.WriteLine(Usage);
        return ExitCodes.Usage;
    }

    private static List<HistoryEntry> Operations(HistoryStore history)
    {
        var operations = new List<HistoryEntry>();
        foreach (var entry in history.List())
        {
            if (entry.Operation != BatchOperation)
            {
                operations.Add(entry);
            }
        }

        return operations;
    }

    /// <summary>
    /// <c>ok</c>, <c>canceled</c>, or <c>failed</c>. The entry has no canceled flag, so an
    /// operation canceled while it ran is recognized by the last line of its log.
    /// </summary>
    private static string ResultOf(HistoryStore history, HistoryEntry entry)
    {
        if (entry.Succeeded)
        {
            return "ok";
        }

        var lines = history.ReadLog(entry).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var wasCanceled = lines.Length > 0 && lines[^1] == CanceledLogLine;
        return wasCanceled ? "canceled" : "failed";
    }

    private static object ToJson(int number, HistoryEntry entry, string result, string? log) => new
    {
        number,
        timestamp = entry.Timestamp,
        operation = entry.Operation,
        packageId = entry.PackageId,
        packageName = entry.PackageName,
        result,
        exitCode = entry.ExitCode,
        durationSeconds = entry.Duration.TotalSeconds,
        arguments = entry.Arguments,
        batchId = entry.BatchId,
        log,
    };

    private static string When(HistoryEntry entry) =>
        entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
