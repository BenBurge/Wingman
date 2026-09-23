using System.IO.Pipes;

namespace Wingman.Core.Elevation;

/// <summary>
/// Creates both ends of the named pipe between the TUI and the elevated helper. The TUI is the
/// server so it can create the pipe before launching the helper, which then needs only the name.
/// </summary>
public static class ElevatedWorkerPipe
{
    public static string NewPipeName() => $"wingman-elevated-{Guid.NewGuid():N}";

    /// <summary>
    /// Creates the TUI's end. It accepts exactly one client, so no other process can open a
    /// second instance under the same name once the helper is connected.
    /// </summary>
    public static NamedPipeServerStream CreateServer(string name) => new(
        name,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    /// <summary>
    /// Creates the helper's end on the local machine; call <c>ConnectAsync</c> to connect it.
    /// </summary>
    public static NamedPipeClientStream CreateClient(string name) => new(
        ".",
        name,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);
}
