using Wingman.Core.SelfUpdate;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman self-update</c>: compares this build with the latest GitHub release and, when it is
/// newer, downloads its installer, checks it against the published SHA-256, and runs it.
/// </summary>
internal sealed class SelfUpdateCommand : ICliCommand
{
    public string Name => "self-update";

    public string Summary => "Update Wingman from GitHub Releases";

    public string Usage => """
        Usage: wingman self-update [--check] [--yes]

        Looks up the latest Wingman release on GitHub and, when it is newer than this one,
        downloads its installer, verifies it, and runs it silently. The installer closes Wingman
        and the tray, replaces the executable, and starts the tray again.

          --check  Only report whether an update is available; exits 10 when one is
          --yes    Also run the installer from a portable copy, which it replaces with an
                   installed one in %LOCALAPPDATA%\Programs\Wingman
        """;

    public IReadOnlyCollection<string> Flags => ["check", "yes"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (context.ReleaseSource is not { } source)
        {
            context.Error.WriteLine("wingman self-update: not available with --fake");
            return ExitCodes.Usage;
        }

        // An explicit command always asks GitHub; the cache only answers when GitHub cannot.
        var check = await SelfUpdateChecker.CheckAsync(
            source, context.Version, context.Rid, context.State, TimeSpan.Zero, context.Now(), context.Cancel);

        if (check.Latest is not { } latest)
        {
            context.Out.WriteLine("Could not reach GitHub Releases");
            return ExitCodes.Failure;
        }

        if (!check.IsNewerAvailable)
        {
            context.Out.WriteLine($"Wingman {check.InstalledVersion} is up to date");
            return ExitCodes.Success;
        }

        if (args.HasFlag("check"))
        {
            context.Out.WriteLine($"Wingman {check.InstalledVersion}; {latest.Version} is available");
            return ExitCodes.UpdatesAvailable;
        }

        if (context.SelfUpdateStarter is not { } starter || context.UpdateDownloader is not { } downloader)
        {
            context.Error.WriteLine("wingman self-update: Windows only");
            return ExitCodes.Usage;
        }

        var kind = InstallDetector.Detect(context.ExePath, context.InstallerRegisteredFolder);
        if (kind == InstallKind.Portable && !args.HasFlag("yes"))
        {
            context.Out.WriteLine($"Wingman {latest.Version} is available: {latest.ReleaseNotesUrl}");
            context.Out.WriteLine("Run the installer to switch this copy to an installed one");
            return ExitCodes.Success;
        }

        var setupPath = await downloader.DownloadVerifiedAsync(latest, context.UpdateDirectory, context.Cancel);

        // Printed first, because the installer stops this process soon after it starts.
        context.Out.WriteLine($"Installing Wingman {latest.Version}; it restarts itself when done.");
        context.Out.Flush();
        starter.StartInstaller(setupPath);
        return ExitCodes.Success;
    }
}
