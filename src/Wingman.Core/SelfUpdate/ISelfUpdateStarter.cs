namespace Wingman.Core.SelfUpdate;

/// <summary>Runs a downloaded Wingman installer. The implementation is Windows-only.</summary>
public interface ISelfUpdateStarter
{
    /// <summary>
    /// Starts <paramref name="setupPath"/> silently and returns without waiting. The installer
    /// closes the tray and every <c>wingman.exe</c> in the install folder, this process included,
    /// so the caller must not rely on running for long afterward.
    /// </summary>
    void StartInstaller(string setupPath);
}
