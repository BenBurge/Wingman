using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Elevation;

/// <summary>
/// One line of the protocol between the TUI and the elevated helper. See
/// <see cref="ElevatedWorkerProtocol"/> for the wire format.
/// </summary>
/// <remarks>
/// The derived records are nested so the short names (<c>Line</c> in particular) cannot clash
/// with Terminal.Gui types in files that import both namespaces.
/// </remarks>
public abstract record WorkerMessage
{
    // Private so the set of messages stays closed and ElevatedWorkerProtocol can handle every one.
    private WorkerMessage()
    {
    }

    /// <summary>TUI to helper: run one winget operation.</summary>
    public sealed record RunOperation(OperationKind Kind, OperationRequest Request) : WorkerMessage;

    /// <summary>Helper to TUI: one line of winget output.</summary>
    public sealed record Line(string Text) : WorkerMessage;

    /// <summary>Helper to TUI: the operation started by the last <see cref="RunOperation"/> ended.</summary>
    public sealed record Finished(int ExitCode, long DurationMs) : WorkerMessage;

    /// <summary>TUI to helper: exit after the current operation.</summary>
    public sealed record Shutdown : WorkerMessage;
}
