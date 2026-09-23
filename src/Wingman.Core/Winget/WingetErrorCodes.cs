using Wingman.Core.Updates;

namespace Wingman.Core.Winget;

/// <summary>
/// Decodes the exit codes winget and the MSI installers it wraps report, into the plain-language
/// explanations shown when a batch operation fails.
/// </summary>
public static class WingetErrorCodes
{
    private const int AssertionFailure = -2147024228;
    private const int AccessDenied = -2147024891;

    private const string RetryElevatedSuggestion =
        "Retry elevated so the whole operation runs as administrator, or restart Wingman as administrator.";

    // Winget's own codes are HRESULTs in the 0x8A15xxxx (APPINSTALLER_CLI_ERROR_*) range and always
    // arrive as negative ints; MSI codes are small positive ints from the Windows Installer.
    //
    // Names and hex values are reconciled against microsoft/winget-cli's
    // src/AppInstallerSharedLib/Public/AppInstallerErrors.h at commit 12dad5ca26ddfe4e64be3f68eb31a54389162ce4.
    private static readonly Dictionary<int, ErrorExplanation> Table = BuildTable();

    private static Dictionary<int, ErrorExplanation> BuildTable()
    {
        var entries = new (int Code, string Name, string WingetSaid, string UsuallyMeans, string Suggestion, UpdatePolicyKind? Policy)[]
        {
            (-1978335231, "INTERNAL_ERROR",
                "An unexpected internal error occurred.",
                "Something went wrong inside winget itself, not the installer.",
                "Retry. If it keeps happening, update winget and check the log for details.",
                null),

            (-1978335230, "INVALID_CL_ARGUMENTS",
                "One or more command line arguments were invalid.",
                "A custom argument was rejected.",
                "Check the package's install options.",
                null),

            (-1978335226, "SHELLEXEC_INSTALL_FAILED",
                "Installer failed.",
                "The installer ran but returned a failure, and winget doesn't know why.",
                "Check the last log lines, retry interactive to see the installer's own message.",
                null),

            (-1978335224, "DOWNLOAD_FAILED",
                "Failed to download the installer.",
                "The download did not complete, usually a network problem.",
                "Check the connection and retry.",
                null),

            (-1978335216, "NO_APPLICABLE_INSTALLER",
                "No applicable installer found.",
                "There is no installer for this architecture or scope.",
                "Try the other scope or architecture.",
                null),

            (-1978335215, "INSTALLER_HASH_MISMATCH",
                "Installer hash does not match; this may indicate a tampered installer.",
                "The publisher replaced the installer file without updating the winget manifest. Common with apps that update themselves.",
                "This app may update itself. Exclude it from Wingman updates and let the app do it, or retry skipping the hash check if you trust the download.",
                UpdatePolicyKind.Exclude),

            // Also the code winget uses when a search or upgrade target matches nothing installed;
            // see tests/Wingman.Core.Tests/Fixtures/exit-codes.txt (search-nomatch).
            (-1978335212, "NO_APPLICATIONS_FOUND",
                "No installed package found matching input criteria.",
                "winget could not find an installed package matching the id or name given.",
                "Check the id and try again, or reload the list if it was just installed.",
                null),

            (-1978335191, "MANIFEST_VALIDATION_FAILURE",
                "Manifest validation failed.",
                "The package's manifest is broken.",
                "Wait for the community repo to fix it.",
                null),

            (-1978335189, "UPDATE_NOT_APPLICABLE",
                "No applicable update found.",
                "The installed version is newer, or the package was installed outside winget.",
                "Retry with the exact version, or exclude it.",
                null),

            (-1978335188, "UPDATE_ALL_HAS_FAILURE",
                "One or more packages failed to update.",
                "\"winget upgrade --all\" hit a failure partway through the batch.",
                "Check the batch log for which package failed, then retry that one.",
                null),

            (-1978335135, "PACKAGE_ALREADY_INSTALLED",
                "The package is already installed.",
                "Nothing to do.",
                "Reload the list.",
                null),

            (-1978335134, "PIN_ALREADY_EXISTS",
                "A pin already exists for this package.",
                "The package already has a pin; adding another was rejected.",
                "Remove the existing pin first if you want to change it.",
                null),

            (-1978335133, "PIN_DOES_NOT_EXIST",
                "No pin exists for this package.",
                "There was nothing to unpin.",
                "Reload the list; the package was not held.",
                null),

            (-1978335128, "PACKAGE_IS_PINNED",
                "The package is pinned.",
                "A winget pin is blocking this update.",
                "Release the hold to let this update through.",
                UpdatePolicyKind.Update),

            (-1978335127, "PACKAGE_IS_STUB",
                "The installed package is a stub package.",
                "The app is a Microsoft Store stub that installs the real package on first launch.",
                "Launch the app once, then retry.",
                null),

            (-1978334975, "INSTALL_PACKAGE_IN_USE",
                "The package is in use.",
                "The app is currently running, so its files can't be replaced.",
                "Close the app and retry.",
                null),

            (-1978334974, "INSTALL_INSTALL_IN_PROGRESS",
                "An installation is already in progress.",
                "Another install for this or another package is still running.",
                "Wait for the other installer, then retry.",
                null),

            (-1978334972, "INSTALL_MISSING_DEPENDENCY",
                "A required dependency is missing.",
                "The installer needs something, such as a runtime, that isn't present.",
                "Install the missing dependency, then retry.",
                null),

            (-1978334971, "INSTALL_DISK_FULL",
                "There is not enough disk space.",
                "The install ran out of room on the target drive.",
                "Free up space, then retry.",
                null),

            (-1978334970, "INSTALL_INSUFFICIENT_MEMORY",
                "There is not enough memory to complete the install.",
                "The installer ran out of memory, usually under heavy load.",
                "Close other apps and retry.",
                null),

            (-1978334969, "INSTALL_NO_NETWORK",
                "No network connection is available.",
                "The installer needs a network connection it couldn't find.",
                "Check the connection and retry.",
                null),

            (-1978334968, "INSTALL_CONTACT_SUPPORT",
                "Contact the software publisher for support.",
                "The installer failed in a way only the publisher can diagnose.",
                "Retry interactive to see the installer's own message, then contact the publisher if it persists.",
                null),

            (-1978334967, "INSTALL_REBOOT_REQUIRED_TO_FINISH",
                "A reboot is required to finish the install.",
                "The package installed but needs a reboot to complete.",
                "Not a failure. Reboot when convenient to finish.",
                null),

            (-1978334966, "INSTALL_REBOOT_REQUIRED_FOR_INSTALL",
                "A reboot is required before the install can proceed.",
                "A pending reboot from an earlier install is blocking this one.",
                "Reboot, then retry.",
                null),

            (-1978334965, "INSTALL_REBOOT_INITIATED",
                "The install triggered a reboot.",
                "The installer restarted the machine as part of finishing up.",
                "Not a failure. Confirm the install afterward if you want to be sure.",
                null),

            (-1978334964, "INSTALL_CANCELLED_BY_USER",
                "The install was cancelled by the user.",
                "The install was interrupted, either from inside the installer or an elevation prompt.",
                "Retry when ready.",
                null),

            (-1978334963, "INSTALL_ALREADY_INSTALLED",
                "This version is already installed.",
                "The installer itself reports the package is already present.",
                "Nothing to do. Reload the list.",
                null),

            (-1978334962, "INSTALL_DOWNGRADE",
                "A higher version of this product is already installed.",
                "A newer version is installed.",
                "Nothing to do unless you specifically want the older version.",
                null),

            (-1978334961, "INSTALL_BLOCKED_BY_POLICY",
                "Installation is blocked by policy.",
                "A device or group policy prevents installing this package.",
                "Check with whoever manages the machine's policies.",
                null),

            (-1978334960, "INSTALL_DEPENDENCIES",
                "The install could not resolve its dependencies.",
                "One or more packages this install depends on failed or are missing.",
                "Install the dependencies first, then retry.",
                null),

            (-1978334959, "INSTALL_PACKAGE_IN_USE_BY_APPLICATION",
                "The package is in use by another application.",
                "A different app has files from this package open.",
                "Close the app and retry.",
                null),

            (-1978334958, "INSTALL_INVALID_PARAMETER",
                "The installer rejected one of its parameters.",
                "An install option passed through to the installer was invalid.",
                "Check the package's install options.",
                null),

            (-1978334957, "INSTALL_SYSTEM_NOT_SUPPORTED",
                "This system is not supported.",
                "The package doesn't support this OS version or architecture.",
                "Check the package's requirements before retrying.",
                null),

            (-1978334956, "INSTALL_UPGRADE_NOT_SUPPORTED",
                "Upgrading this package is not supported.",
                "The installer can't upgrade in place from the installed version.",
                "Uninstall the current version, then install the new one.",
                null),

            (-1978334955, "INSTALL_CUSTOM_ERROR",
                "The installer reported a custom error.",
                "The installer failed with its own error code that winget doesn't decode further.",
                "Check the last log lines, retry interactive to see the installer's own message.",
                null),

            // Win32 errors wrapped as HRESULTs (0x8007xxxx), which winget passes through when the
            // installer's own elevation fails rather than the installer itself.
            (AssertionFailure, "ERROR_ASSERTION_FAILURE",
                "An assertion failure has occurred.",
                "winget could not complete the installer's elevation; a UAC or Admin By Request prompt was intercepted or declined, or the installer ran outside winget.",
                RetryElevatedSuggestion,
                null),

            (AccessDenied, "E_ACCESSDENIED",
                "Access is denied.",
                "The operation needs administrator rights.",
                RetryElevatedSuggestion,
                null),

            (1602, "ERROR_INSTALL_USEREXIT",
                "User cancelled installation.",
                "The install was cancelled from inside the installer's own UI.",
                "Retry when ready.",
                null),

            (1603, "ERROR_INSTALL_FAILURE",
                "Fatal error during installation.",
                "Usually the app is running, or a previous install is broken.",
                "Close it and retry interactive.",
                null),

            (1618, "ERROR_INSTALL_ALREADY_RUNNING",
                "Another installation is already in progress.",
                "Windows Installer is busy with another install.",
                "Wait for the other installer, then retry.",
                null),

            (1619, "ERROR_INSTALL_PACKAGE_OPEN_FAILED",
                "This installation package could not be opened.",
                "The downloaded installer file is missing or corrupt.",
                "Retry to redownload; check the connection if it keeps failing.",
                null),

            (1638, "ERROR_PRODUCT_VERSION",
                "Another version of this product is already installed.",
                "A newer or the same version is already present.",
                "Exclude it if the app self-updates.",
                UpdatePolicyKind.Exclude),

            (3010, "ERROR_SUCCESS_REBOOT_REQUIRED",
                "A restart is required to complete the install.",
                "The package installed successfully but needs a reboot to finish.",
                "Not a failure. Reboot when convenient to finish.",
                null),
        };

        var table = new Dictionary<int, ErrorExplanation>(entries.Length);
        foreach (var entry in entries)
        {
            table[entry.Code] = new ErrorExplanation(
                Format(entry.Code), entry.Name, entry.WingetSaid, entry.UsuallyMeans, entry.Suggestion, entry.Policy);
        }
        return table;
    }

    /// <summary>Looks up the plain-language explanation for a winget or MSI exit code.</summary>
    public static ErrorExplanation Explain(int exitCode) =>
        Table.TryGetValue(exitCode, out var explanation)
            ? explanation
            : new ErrorExplanation(
                Format(exitCode),
                "Unknown",
                "",
                $"winget exited with {Format(exitCode)}",
                "Read the log; retry interactive to see the installer's own message.",
                null);

    /// <summary>
    /// True for exit codes that a retry through the elevated helper usually fixes, so the failure
    /// panel can put that action first.
    /// </summary>
    public static bool SuggestsElevation(int exitCode) =>
        exitCode is AssertionFailure or AccessDenied;

    /// <summary>
    /// Formats an exit code the way winget's own output does: negative HRESULTs as
    /// <c>0x</c> plus eight uppercase hex digits of the unsigned value, everything else as decimal.
    /// </summary>
    public static string Format(int exitCode) =>
        exitCode < 0
            ? "0x" + unchecked((uint)exitCode).ToString("X8")
            : exitCode.ToString();
}
