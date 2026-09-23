using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Wingman.Core.Setup;

namespace Wingman.Windows.Setup;

/// <summary>
/// Decides whether a task already registered in Task Scheduler, as printed by
/// <c>schtasks /Query /XML</c>, runs the same command on the same schedule as a
/// <see cref="ScheduledTaskSpec"/>. The comparison is loose on purpose: Task Scheduler rewrites
/// what <c>schtasks /Create</c> gave it, so only the command line and the trigger interval count.
/// </summary>
internal static class ScheduledTaskXml
{
    public static bool Matches(string queryOutput, ScheduledTaskSpec task)
    {
        var document = TryParse(queryOutput);
        if (document is null)
        {
            return false;
        }

        var exec = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Exec");
        if (exec is null)
        {
            return false;
        }

        var registeredCommand = $"{ChildText(exec, "Command")} {ChildText(exec, "Arguments")}";
        var sameCommand = string.Equals(
            NormalizeCommandLine(registeredCommand), NormalizeCommandLine(task.Command), StringComparison.OrdinalIgnoreCase);

        return sameCommand && HasTrigger(document, task.SchtasksCreateArgs);
    }

    private static XDocument? TryParse(string queryOutput)
    {
        var start = queryOutput.IndexOf('<');
        if (start < 0)
        {
            return null;
        }

        try
        {
            return XDocument.Parse(queryOutput[start..]);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // Task Scheduler splits the /TR string into Command and Arguments and may keep or drop the
    // quotes around the executable, so both sides are compared without quotes or repeated spaces.
    // It also resolves a bare "conhost.exe" to its full System32 path when it stores the task, so
    // a freshly registered headless task would otherwise never compare equal to the plan that
    // produced it; both sides collapse to the same "conhost.exe" token before comparing. A task
    // registered by a previous build has no conhost token at all, so it still compares unequal and
    // gets updated rather than left alone.
    private static string NormalizeCommandLine(string commandLine)
    {
        var words = commandLine.Replace("\"", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && words[0].EndsWith("conhost.exe", StringComparison.OrdinalIgnoreCase))
        {
            words[0] = "conhost.exe";
        }

        return string.Join(' ', words);
    }

    private static bool HasTrigger(XDocument document, IReadOnlyList<string> createArgs)
    {
        var schedule = ArgAfter(createArgs, "/SC");
        switch (schedule)
        {
            case "HOURLY":
                if (!int.TryParse(ArgAfter(createArgs, "/MO"), CultureInfo.InvariantCulture, out var hours))
                {
                    return false;
                }

                return ElementsNamed(document, "Interval").Any(interval => IsDuration(interval.Value, TimeSpan.FromHours(hours)));

            case "DAILY":
                var startTime = $"T{ArgAfter(createArgs, "/ST")}:";
                return ElementsNamed(document, "CalendarTrigger")
                    .Any(trigger => ChildText(trigger, "StartBoundary").Contains(startTime, StringComparison.Ordinal));

            case "ONLOGON":
                return ElementsNamed(document, "LogonTrigger").Any();

            default:
                return false;
        }
    }

    private static bool IsDuration(string value, TimeSpan expected)
    {
        try
        {
            return XmlConvert.ToTimeSpan(value) == expected;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IEnumerable<XElement> ElementsNamed(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    private static string ChildText(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim() ?? "";

    private static string ArgAfter(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag)
            {
                return args[i + 1];
            }
        }

        return "";
    }
}
