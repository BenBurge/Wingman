namespace Wingman.Core.SelfUpdate;

/// <summary>
/// The command that upgrades Wingman itself. A later, Windows-only issue starts this detached
/// (<c>UseShellExecute = false</c>, <c>CreateNoWindow = true</c>) so the upgrade can replace the
/// running executable after this process exits; nothing here executes anything.
/// </summary>
public static class SelfUpdateCommand
{
    // The delay gives this process time to exit and release its executable before winget tries
    // to replace it; cmd.exe carries both steps because the running app cannot wait on its own
    // exit.
    private const string WingetUpgradeCommand =
        $"winget upgrade --id {SelfUpdateChecker.PackageId} --exact --source winget " +
        "--accept-source-agreements --accept-package-agreements --disable-interactivity";

    /// <summary>The argv for a detached <c>cmd.exe</c> run of <see cref="WingetUpgradeCommand"/>.</summary>
    public static string[] DetachedUpgradeArgv() =>
        ["/d", "/c", $"timeout /t 2 /nobreak >nul & {WingetUpgradeCommand}"];

    /// <summary>The command to show on the status line, without the detachment plumbing.</summary>
    public static string Describe() => WingetUpgradeCommand;
}
