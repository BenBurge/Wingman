namespace Wingman.Core.History;

/// <summary>
/// One completed operation, as read from or written to the history store by
/// <see cref="HistoryStore"/>.
/// </summary>
/// <param name="Operation">A lowercase verb: <c>install</c>, <c>upgrade</c>, <c>uninstall</c>,
/// <c>pin</c>, <c>unpin</c>, or <c>batch</c>.</param>
/// <param name="LogFileName">The name of the paired <c>.log</c> file in the same directory as
/// this entry's <c>.json</c> file.</param>
/// <param name="BatchId">Groups entries that ran as part of the same batch; <see langword="null"/>
/// for an operation that ran on its own.</param>
public sealed record HistoryEntry(
    DateTimeOffset Timestamp,
    string Operation,
    string PackageId,
    string PackageName,
    int ExitCode,
    bool Succeeded,
    TimeSpan Duration,
    IReadOnlyList<string> Arguments,
    string LogFileName,
    string? BatchId);
