namespace Wingman.Core.Setup;

/// <summary>
/// Decides what <see cref="ISetupExecutor"/> does with one part of a plan once it has read the
/// part's current state, so the rule is the same for tasks, registry values, and the shortcut.
/// </summary>
public static class SetupOutcomes
{
    /// <summary>
    /// The <see cref="SetupResult"/> outcome for a part, assuming any write it implies succeeds.
    /// <see cref="SetupResult.Removed"/> tells the caller to delete the part, and
    /// <see cref="SetupResult.Created"/> or <see cref="SetupResult.Updated"/> to write it; every
    /// other outcome means nothing is written.
    /// </summary>
    /// <param name="exists">The part is registered now, in any form.</param>
    /// <param name="enabled">The setting behind the part is on; false removes it even without <paramref name="remove"/>.</param>
    /// <param name="remove">Setup was run with <c>--remove</c>.</param>
    /// <param name="dryRun">Nothing is written; the outcome says what would happen.</param>
    /// <param name="upToDate">The registered part already matches the plan; ignored when it does not exist.</param>
    public static string Decide(bool exists, bool enabled, bool remove, bool dryRun, bool upToDate)
    {
        var removes = remove || !enabled;
        if (removes)
        {
            if (!exists)
            {
                return SetupResult.Unchanged;
            }

            return dryRun ? SetupResult.WouldRemove : SetupResult.Removed;
        }

        if (exists && upToDate)
        {
            return SetupResult.Unchanged;
        }

        if (dryRun)
        {
            return SetupResult.WouldCreate;
        }

        return exists ? SetupResult.Updated : SetupResult.Created;
    }
}
