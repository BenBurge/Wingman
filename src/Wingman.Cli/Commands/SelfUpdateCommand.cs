using Wingman.Core.Notifications;
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
        Usage: wingman self-update [--check] [--yes] [--announce]

        Looks up the latest Wingman release on GitHub and, when it is newer than this one,
        downloads its installer, verifies it, and runs it silently. The installer closes Wingman
        and the tray, replaces the executable, and starts the tray again.

          --check     Only report whether an update is available; exits 10 when one is
          --yes       Also run the installer from a portable copy, which it replaces with an
                      installed one in %LOCALAPPDATA%\Programs\Wingman
          --announce  Show the "Wingman updated" toast when this is the version an automatic
                      update just installed; the installer runs this. Prints nothing
        """;

    public IReadOnlyCollection<string> Flags => ["check", "yes", "announce"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        // Every install runs this, so with nothing to announce it must stay silent and succeed.
        if (args.HasFlag("announce"))
        {
            await AnnounceFinishedUpdateAsync(context);
            return ExitCodes.Success;
        }

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
        context.State.Update(state => state.PendingUpdateVersion = latest.Version);

        // Printed first, because the installer stops this process soon after it starts.
        context.Out.WriteLine($"Installing Wingman {latest.Version}; it restarts itself when done.");
        context.Out.Flush();
        starter.StartInstaller(setupPath);
        return ExitCodes.Success;
    }

    /// <summary>
    /// Sends the <c>Wingman updated</c> toast when this is the version <c>check --notify</c> or
    /// <c>self-update</c> started the installer for, and clears the marker so the toast is shown once. The installer's
    /// <c>self-update --announce</c> and every <c>check --notify</c> both call this, so the toast
    /// still appears when the installer's run was missed.
    /// </summary>
    internal static async Task AnnounceFinishedUpdateAsync(CliContext context)
    {
        var pending = context.State.Load().PendingUpdateVersion;
        if (pending.Length == 0 || !SelfUpdateChecker.IsSameVersion(pending, context.Version))
        {
            return;
        }

        context.State.Update(state => state.PendingUpdateVersion = "");
        if (!context.Settings.NotificationsPaused && context.ToastSender is { } toasts)
        {
            await toasts.SendAsync(ToastBuilder.WingmanUpdated(context.Version), context.Cancel);
        }
    }
}
