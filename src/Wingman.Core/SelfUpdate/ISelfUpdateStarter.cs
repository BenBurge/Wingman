namespace Wingman.Core.SelfUpdate;

/// <summary>
/// Starts <see cref="SelfUpdateCommand.DetachedUpgradeArguments"/> in a process that outlives this
/// one. The implementation is Windows-only.
/// </summary>
public interface ISelfUpdateStarter
{
    /// <summary>Starts the upgrade and returns at once; the caller should exit so winget can replace the executable.</summary>
    void StartDetachedUpgrade();
}
