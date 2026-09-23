using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Wingman.Windows.Elevation;

/// <summary>
/// Creates the TUI's end of the elevation pipe with an ACL that only the current user and
/// Administrators can open, instead of the default one that lets every local user read it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecurePipeServer
{
    /// <summary>
    /// Creates a single-instance asynchronous byte pipe. Administrators is granted as well as the
    /// current user because an over-the-shoulder UAC or Admin By Request prompt can start the
    /// helper under a different administrator account.
    /// </summary>
    public static NamedPipeServerStream Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User
            ?? throw new InvalidOperationException("The current process token has no user SID.");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null);

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }
}
