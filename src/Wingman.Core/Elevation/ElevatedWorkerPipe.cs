using System.IO.Pipes;
using System.Security.Principal;

namespace Wingman.Core.Elevation;

/// <summary>
/// Creates both ends of the named pipe between the TUI and the elevated helper. The TUI is the
/// server so it can create the pipe before launching the helper, which then needs only the name.
/// </summary>
public static class ElevatedWorkerPipe
{
    public static string NewPipeName() => $"wingman-elevated-{Guid.NewGuid():N}";

    /// <summary>
    /// Creates a server end with the default access control. It accepts exactly one client, so no
    /// other process can open a second instance under the same name once the helper is connected.
    /// The default ACL lets any local user connect, so the Windows launcher uses
    /// <c>SecurePipeServer</c> instead; this one serves the loopback tests.
    /// </summary>
    public static NamedPipeServerStream CreateServer(string name) => new(
        name,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    /// <summary>
    /// Creates the helper's end on the local machine; call <c>ConnectAsync</c> to connect it.
    /// The server may only identify the client, never impersonate it, because the client is the
    /// elevated helper and a server that impersonated it would act with administrator rights.
    /// </summary>
    public static NamedPipeClientStream CreateClient(string name) => new(
        ".",
        name,
        PipeDirection.InOut,
        PipeOptions.Asynchronous,
        TokenImpersonationLevel.Identification,
        HandleInheritability.None);
}
