namespace Wingman.Core.SelfUpdate;

/// <summary>How the running copy of Wingman got onto the machine.</summary>
public enum InstallKind
{
    /// <summary>Placed by hand, such as from the release zip; the installer does not own its folder.</summary>
    Portable,

    /// <summary>Installed by the Inno Setup installer, so running a newer installer replaces it in place.</summary>
    Installer,
}

/// <summary>Decides the running copy's <see cref="InstallKind"/>.</summary>
public static class InstallDetector
{
    /// <summary>
    /// <see cref="InstallKind.Installer"/> when <paramref name="exePath"/> sits directly in
    /// <paramref name="installerRegisteredFolder"/>, the <c>InstallLocation</c> of the installer's
    /// uninstall entry; otherwise <see cref="InstallKind.Portable"/>.
    /// </summary>
    /// <remarks>
    /// Compares the text of Windows paths without resolving them, so it behaves the same when the
    /// tests run on macOS or Linux; Inno Setup writes the folder with a trailing backslash.
    /// </remarks>
    public static InstallKind Detect(string exePath, string? installerRegisteredFolder)
    {
        if (string.IsNullOrWhiteSpace(installerRegisteredFolder))
        {
            return InstallKind.Portable;
        }

        var separator = exePath.LastIndexOfAny(['\\', '/']);
        if (separator < 0)
        {
            return InstallKind.Portable;
        }

        var exeFolder = TrimSeparators(exePath[..separator]);
        var registeredFolder = TrimSeparators(installerRegisteredFolder.Trim());
        var isSameFolder = string.Equals(Unify(exeFolder), Unify(registeredFolder), StringComparison.OrdinalIgnoreCase);
        return isSameFolder ? InstallKind.Installer : InstallKind.Portable;
    }

    private static string TrimSeparators(string path) => path.TrimEnd('\\', '/');

    private static string Unify(string path) => path.Replace('/', '\\');
}
