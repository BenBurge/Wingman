using Wingman.Core.Models;
using Wingman.Core.Notifications;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Updates;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman check</c>: lists available upgrades the way the Updates tab does, records the result
/// in <c>state.json</c> for the tray, and exits 10 when any update is waiting. With <c>--notify</c>,
/// as the scheduled task runs it, it also asks GitHub Releases for a newer Wingman: an installed
/// copy with <c>AutoUpdateWingman</c> on updates itself, and any other copy is offered the release
/// with a toast once. It also announces a finished update when the installer's own announcement
/// was missed.
/// </summary>
internal sealed class CheckCommand : ICliCommand
{
    private const string HeldMarker = "⊘";

    public string Name => "check";

    public string Summary => "Check for updates; exits 10 when any are available";

    public string Usage => """
        Usage: wingman check [--json] [--notify]

        Lists available upgrades. Held packages are listed with ⊘ but not counted; excluded
        packages and skipped versions are left out. Exits 10 when any update is available.

          --json    Print the updates and counts as one JSON document
          --notify  Show a toast when updates are available, and update Wingman itself when
                    it was installed by the installer and auto-update is on, or else show a
                    toast offering a newer Wingman
        """;

    public IReadOnlyCollection<string> Flags => ["notify"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        DateTimeOffset checkedAt;
        UpdatesView view;
        var available = new List<PackageRow>();

        if (context.Notify)
        {
            await SelfUpdateCommand.AnnounceFinishedUpdateAsync(context);
        }

        // The tray shows a working badge while this is set; the finally clears it even when the
        // check throws, so a crash never leaves the badge stuck.
        context.State.Update(state => state.Running = true);
        try
        {
            view = await ListUpdatesAsync(context);
            checkedAt = DateTimeOffset.Now;
            foreach (var update in view.Visible)
            {
                if (update.Policy != UpdatePolicyKind.Hold)
                {
                    available.Add(update.Row);
                }
            }

            context.State.Update(state =>
            {
                state.LastCheck = checkedAt;
                state.UpdatesAvailable = available.Count;
                state.UpdateIds = [.. available.Select(row => row.Id)];
                state.LastError = "";
            });
        }
        finally
        {
            context.State.Update(state => state.Running = false);
        }

        if (context.Json)
        {
            WriteJson(context, view, checkedAt);
        }
        else
        {
            WriteText(context, view, available.Count);
        }

        var settings = context.Settings;
        var wantsToast = context.Notify && available.Count > 0 && settings.ToastOnUpdates && !settings.NotificationsPaused;
        if (wantsToast && context.ToastSender is { } toasts)
        {
            await toasts.SendAsync(ToastBuilder.UpdatesAvailable(available), context.Cancel);
        }

        if (context.Notify)
        {
            await CheckForNewerWingmanAsync(context);
        }

        return available.Count > 0 ? ExitCodes.UpdatesAvailable : ExitCodes.Success;
    }

    /// <summary>
    /// Asks GitHub for the latest release on every run, so a release is noticed within one check
    /// interval; when GitHub cannot answer, the cached release still counts. A newer release is
    /// installed when auto-update can apply it and offered with a toast otherwise.
    /// </summary>
    private static async Task CheckForNewerWingmanAsync(CliContext context)
    {
        if (context.ReleaseSource is not { } source)
        {
            return;
        }

        var check = await SelfUpdateChecker.CheckAsync(
            source, context.Version, context.Rid, context.State, TimeSpan.Zero, context.Now(), context.Cancel);
        if (!check.IsNewerAvailable || check.Latest is not { } latest)
        {
            return;
        }

        // A portable copy is never replaced behind the user's back; self-update --yes does that.
        var isInstalled = InstallDetector.Detect(context.ExePath, context.InstallerRegisteredFolder) == InstallKind.Installer;
        var autoUpdates = context.Settings.AutoUpdateWingman && isInstalled;
        if (autoUpdates && context.UpdateDownloader is { } downloader && context.SelfUpdateStarter is { } starter)
        {
            await InstallAsync(context, latest, downloader, starter);
            return;
        }

        await OfferUpdateAsync(context, check.InstalledVersion, latest);
    }

    /// <summary>
    /// Sends the "is available" toast unless this release was already offered or notifications are
    /// paused. The release is marked offered only once a toast was actually sent, so a release
    /// that arrives while paused is still offered after resuming.
    /// </summary>
    private static async Task OfferUpdateAsync(CliContext context, string installedVersion, ReleaseInfo latest)
    {
        var offered = context.State.Load().NotifiedUpdateVersion;
        var wasOffered = offered.Length > 0 && SelfUpdateChecker.IsSameVersion(offered, latest.Version);
        if (wasOffered || context.Settings.NotificationsPaused || context.ToastSender is not { } toasts)
        {
            return;
        }

        var toast = ToastBuilder.WingmanUpdateAvailable(latest.Version, installedVersion, latest.ReleaseNotesUrl);
        await toasts.SendAsync(toast, context.Cancel);
        context.State.Update(state => state.NotifiedUpdateVersion = latest.Version);
    }

    /// <summary>
    /// Downloads, verifies, and starts the installer for <paramref name="latest"/>. The installer
    /// announces the update itself once the new version is in place. A failure is recorded in
    /// <c>state.json</c> for the tray and never fails the check.
    /// </summary>
    private static async Task InstallAsync(
        CliContext context, ReleaseInfo latest, UpdateDownloader downloader, ISelfUpdateStarter starter)
    {
        try
        {
            var setupPath = await downloader.DownloadVerifiedAsync(latest, context.UpdateDirectory, context.Cancel);
            context.State.Update(state => state.PendingUpdateVersion = latest.Version);
            starter.StartInstaller(setupPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.Cancel.IsCancellationRequested)
        {
            context.State.Update(state =>
            {
                state.PendingUpdateVersion = "";
                state.LastError = $"Could not update Wingman to {latest.Version}: {ex.Message}";
            });
        }
    }

    private static async Task<UpdatesView> ListUpdatesAsync(CliContext context)
    {
        using var wait = WingetWait.Begin(context);
        try
        {
            var upgrades = await context.Client.ListUpgradesAsync(context.Cancel);

            // Nothing can be held or excluded when there is nothing to update, so skip the pin lookup.
            IReadOnlyList<Pin> pins = upgrades.Count > 0
                ? await context.Client.ListPinsAsync(context.Cancel)
                : [];
            return UpdatesFilter.Apply(upgrades, pins, context.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.State.Update(state => state.LastError = ex.Message);
            throw;
        }
    }

    private static void WriteText(CliContext context, UpdatesView view, int availableCount)
    {
        if (view.Visible.Count == 0)
        {
            context.Out.WriteLine("Everything is up to date.");
            return;
        }

        var table = new TableWriter("Name", "Id", "Version", "Available");
        foreach (var update in view.Visible)
        {
            var row = update.Row;
            var marker = update.Policy == UpdatePolicyKind.Hold ? HeldMarker : "";
            table.AddMarkedRow(marker, row.Name, row.Id, row.Version, row.AvailableVersion ?? "");
        }

        table.Write(context.Out, context.Width);
        context.Out.WriteLine();

        var noun = availableCount == 1 ? "update" : "updates";
        var footer = $"{availableCount} {noun} available · {view.HeldCount} held · {view.ExcludedCount} excluded";
        if (view.SkippedCount > 0)
        {
            footer += $" · {view.SkippedCount} skipped";
        }

        context.Out.WriteLine(footer);
    }

    private static void WriteJson(CliContext context, UpdatesView view, DateTimeOffset checkedAt)
    {
        var updates = new List<object>(view.Visible.Count);
        foreach (var update in view.Visible)
        {
            var row = update.Row;
            updates.Add(new
            {
                name = row.Name,
                id = row.Id,
                version = row.Version,
                available = row.AvailableVersion,
                source = row.Source,
                policy = update.Policy,
            });
        }

        JsonOutput.Write(context.Out, new
        {
            updates,
            held = view.HeldCount,
            excluded = view.ExcludedCount,
            checkedAt,
        });
    }
}
