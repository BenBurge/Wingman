using System.Diagnostics;
using System.Runtime.Versioning;
using Wingman.Core.SelfUpdate;

namespace Wingman.Windows.SelfUpdate;

/// <summary>
/// Runs a downloaded Wingman installer silently and does not wait for it. The installer closes the
/// tray and every <c>wingman.exe</c> in the install folder, this process included, replaces the
/// executable, runs <c>wingman setup</c>, and starts the tray again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SelfUpdateStarter : ISelfUpdateStarter
{
    public void StartInstaller(string setupPath)
    {
        // Inno Setup ignores /CURRENTUSER while wingman.iss allows no install-mode override; it is
        // passed so the install stays per-user if the script ever allows one.
        var startInfo = new ProcessStartInfo(setupPath)
        {
            UseShellExecute = false,
            ArgumentList = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER" },
        };

        Process.Start(startInfo)?.Dispose();
    }
}
