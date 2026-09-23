using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Elevation;

/// <summary>
/// The TUI's connection to the elevated helper, which runs the operations that need elevation.
/// </summary>
public interface IElevatedOperationChannel : IAsyncDisposable
{
    /// <summary>
    /// Has the helper run one operation, reporting each output line to <paramref name="output"/>
    /// as it arrives. Operations run one at a time; concurrent callers wait their turn.
    /// </summary>
    /// <exception cref="IOException">The helper disconnected before the operation finished.</exception>
    Task<OperationResult> RunAsync(
        OperationKind kind,
        OperationRequest request,
        IProgress<string> output,
        CancellationToken ct);

    /// <summary>
    /// Tells the helper to exit.
    /// </summary>
    Task ShutdownAsync(CancellationToken ct);
}
