using Wingman.Core.Settings;

namespace Wingman.Core.Elevation;

/// <summary>The platform-neutral rules for which client of the elevation pipe is the helper the launcher started.</summary>
public static class PipePeerCheck
{
    /// <summary>
    /// For <see cref="ElevationLauncher.Direct"/>, true exactly when the client is the process that
    /// was started, without reading anything else about it: a helper elevated under another
    /// administrator account may refuse to be opened. For <see cref="ElevationLauncher.PowerShell"/>
    /// the started process is the wrapper and the helper its child, so the client must instead run
    /// <paramref name="expectedImagePath"/> in this process's session.
    /// </summary>
    /// <param name="readImagePath">Reads a process's image path, or null when it cannot.</param>
    /// <param name="isInOwnSession">False also when the process's session cannot be read.</param>
    public static bool IsHelper(
        ElevationLauncher launcher,
        uint clientProcessId,
        int startedProcessId,
        string? expectedImagePath,
        Func<uint, string?> readImagePath,
        Func<uint, bool> isInOwnSession)
    {
        if (launcher == ElevationLauncher.Direct)
        {
            return clientProcessId == (uint)startedProcessId;
        }

        return ImagePathMatches(expectedImagePath, readImagePath(clientProcessId))
            && isInOwnSession(clientProcessId);
    }

    /// <summary>
    /// True when <paramref name="actual"/> names the same executable as <paramref name="expected"/>.
    /// Windows paths are case-insensitive, so the comparison is too; a path that could not be
    /// read never matches.
    /// </summary>
    public static bool ImagePathMatches(string? expected, string? actual) =>
        !string.IsNullOrEmpty(expected)
        && !string.IsNullOrEmpty(actual)
        && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
