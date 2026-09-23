namespace Wingman.Core.Tests;

/// <summary>
/// Records reports synchronously. <see cref="Progress{T}"/> posts each report to a thread-pool or
/// synchronization-context callback, so tests could otherwise assert before the lines arrive.
/// </summary>
internal sealed class RecordingProgress : IProgress<string>
{
    public List<string> Lines { get; } = [];

    public void Report(string value) => Lines.Add(value);
}
