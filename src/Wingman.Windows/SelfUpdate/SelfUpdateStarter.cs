using System.Diagnostics;
using System.Runtime.Versioning;
using Wingman.Core.SelfUpdate;

namespace Wingman.Windows.SelfUpdate;

/// <summary>
/// Starts the self-upgrade in a hidden <c>cmd.exe</c> that keeps running after Wingman exits,
/// because winget cannot replace the executable while this process holds it open.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SelfUpdateStarter : ISelfUpdateStarter
{
    public void StartDetachedUpgrade()
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in SelfUpdateCommand.DetachedUpgradeArgv())
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo)?.Dispose();
    }
}
