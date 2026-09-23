using Wingman.Core.Models;
using Wingman.Core.Notifications;
using Wingman.Core.Updates;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman check</c>: lists available upgrades the way the Updates tab does, records the result
/// in <c>state.json</c> for the tray, and exits 10 when any update is waiting.
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
          --notify  Show a toast when updates are available
        """;

    public IReadOnlyCollection<string> Flags => ["notify"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        UpdatesView view;
        try
        {
            var upgrades = await context.Client.ListUpgradesAsync(context.Cancel);
            var pins = await context.Client.ListPinsAsync(context.Cancel);
            view = UpdatesFilter.Apply(upgrades, pins, context.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.State.Update(state => state.LastError = ex.Message);
            throw;
        }

        var checkedAt = DateTimeOffset.Now;
        var available = new List<PackageRow>();
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

        var settings = context.Settings;
        var wantsToast = context.Notify && available.Count > 0 && settings.ToastOnUpdates && !settings.NotificationsPaused;
        if (wantsToast && context.Notifier is { } notify)
        {
            notify(ToastBuilder.UpdatesAvailable(available));
        }

        if (context.Json)
        {
            WriteJson(context, view, checkedAt);
        }
        else
        {
            WriteText(context, view, available.Count);
        }

        return available.Count > 0 ? ExitCodes.UpdatesAvailable : ExitCodes.Success;
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
