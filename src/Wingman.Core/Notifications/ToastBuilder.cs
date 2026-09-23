using System.Xml.Linq;
using Wingman.Core.Models;

namespace Wingman.Core.Notifications;

/// <summary>
/// Builds toast content and the Windows toast XML for it, plus the PowerShell invocation that
/// shows it. Nothing here shows a toast; the Windows side (a later issue) runs the argv this
/// produces.
/// </summary>
public static class ToastBuilder
{
    private const string DefaultLaunchUri = "wingman:updates";

    public static ToastContent UpdatesAvailable(IReadOnlyList<PackageRow> updates)
    {
        var title = updates.Count == 1 ? "1 update available" : $"{updates.Count} updates available";

        var shownNames = updates.Take(3).Select(update => update.Name);
        var body = string.Join(", ", shownNames);
        if (updates.Count > 3)
        {
            body += $" and {updates.Count - 3} more";
        }

        var actions = new List<ToastAction>
        {
            new("Update all", "wingman:update-all"),
            new("Open", "wingman:updates"),
        };

        return new ToastContent(title, body, actions, "wingman-updates", "wingman");
    }

    public static ToastContent BatchFinished(int total, int succeeded, int failed, int canceled)
    {
        string title;
        if (failed > 0)
        {
            title = $"{failed} of {total} failed";
        }
        else if (canceled > 0)
        {
            title = "Batch canceled";
        }
        else
        {
            title = $"{succeeded} of {total} updated";
        }

        var body = $"{succeeded} succeeded, {failed} failed, {canceled} canceled";
        var actions = new List<ToastAction> { new("View log", "wingman:history") };

        return new ToastContent(title, body, actions, "wingman-batch", "wingman");
    }

    public static string ToXml(ToastContent content)
    {
        var launch = content.Actions.Count > 0 ? content.Actions[0].ProtocolUri : DefaultLaunchUri;

        var toast = new XElement("toast",
            new XAttribute("activationType", "protocol"),
            new XAttribute("launch", launch),
            new XElement("visual",
                new XElement("binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", content.Title),
                    new XElement("text", content.Body))),
            new XElement("actions",
                content.Actions.Select(action => new XElement("action",
                    new XAttribute("content", action.Content),
                    new XAttribute("activationType", "protocol"),
                    new XAttribute("arguments", action.ProtocolUri)))));

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    public static string[] BuildPowerShellCommand(ToastContent content, string appUserModelId)
    {
        var xml = SingleQuote(ToXml(content));
        var tag = SingleQuote(content.Tag);
        var group = SingleQuote(content.Group);
        var appId = SingleQuote(appUserModelId);

        var script = string.Join("; ",
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null",
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null",
            "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument",
            $"$xml.LoadXml({xml})",
            "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml",
            $"$toast.Tag = {tag}",
            $"$toast.Group = {group}",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier({appId}).Show($toast)");

        return ["powershell.exe", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script];
    }

    // PowerShell single-quoted strings treat everything literally except a doubled quote, which
    // is how a literal quote is embedded.
    private static string SingleQuote(string value) => $"'{value.Replace("'", "''")}'";
}
