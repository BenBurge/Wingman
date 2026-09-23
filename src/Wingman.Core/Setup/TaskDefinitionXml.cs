using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Wingman.Core.Setup;

/// <summary>
/// Writes the Task Scheduler definition <c>schtasks /Create /XML</c> registers for a
/// <see cref="ScheduledTaskSpec"/>, and compares one that <c>schtasks /Query /XML</c> printed
/// against a spec. The document is what the <c>schtasks</c> flags cannot say: an interval up to
/// 168 hours, a logon trigger for the current user only (any-user logon triggers need
/// administrator rights), and a laptop that runs the task on battery and catches up on missed runs.
/// </summary>
public static class TaskDefinitionXml
{
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    /// The task definition, with a UTF-16 declaration because <c>schtasks /XML</c> reads the file
    /// as UTF-16; the caller must write it in that encoding.
    /// </summary>
    /// <param name="userId">The account the task runs as and whose logon triggers it, <c>DOMAIN\user</c>.</param>
    /// <param name="today">The local date the interval and daily triggers start from; defaults to today.</param>
    public static string Build(ScheduledTaskSpec spec, string userId, DateOnly? today = null)
    {
        var startDate = today ?? DateOnly.FromDateTime(DateTime.Now);

        var task = new XElement(
            Ns + "Task",
            new XAttribute("version", "1.4"),
            new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Description", spec.Description)),
            new XElement(Ns + "Triggers", BuildTrigger(spec, userId, startDate)),
            new XElement(
                Ns + "Principals",
                new XElement(
                    Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userId),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(
                Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "true"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "RunOnlyIfNetworkAvailable", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "Hidden", "false"),
                new XElement(Ns + "ExecutionTimeLimit", "PT2H"),
                new XElement(Ns + "Priority", "7")),
            new XElement(
                Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    Ns + "Exec",
                    new XElement(Ns + "Command", spec.Executable),
                    new XElement(Ns + "Arguments", spec.Arguments))));

        var declaration = new XDeclaration("1.0", "UTF-16", null);
        return declaration + Environment.NewLine + task;
    }

    /// <summary>
    /// True when <paramref name="queryOutput"/>, the definition of a registered task, starts the
    /// same program with the same arguments on the same trigger as <paramref name="spec"/>, and
    /// carries the battery and missed-run settings <see cref="Build"/> writes. Task Scheduler adds
    /// its own <c>Date</c>, <c>Author</c>, and <c>URI</c> and moves the start boundary around, so
    /// only these parts count. A task an older <c>schtasks</c>-flag setup registered lacks the
    /// settings and compares unequal, so setup rewrites it.
    /// </summary>
    public static bool Matches(string queryOutput, ScheduledTaskSpec spec, string userId)
    {
        var task = TryParse(queryOutput)?.Root;
        if (task is null || task.Name != Ns + "Task")
        {
            return false;
        }

        var exec = task.Element(Ns + "Actions")?.Element(Ns + "Exec");
        if (exec is null)
        {
            return false;
        }

        var sameProgram = string.Equals(
            ChildText(exec, "Command"), spec.Executable, StringComparison.OrdinalIgnoreCase);
        var sameArguments = ChildText(exec, "Arguments") == spec.Arguments;
        if (!sameProgram || !sameArguments)
        {
            return false;
        }

        var settings = task.Element(Ns + "Settings");
        if (settings is null || !HasLaptopSettings(settings))
        {
            return false;
        }

        var triggers = task.Element(Ns + "Triggers");
        return triggers is not null && HasTrigger(triggers, spec, userId);
    }

    private static XElement BuildTrigger(ScheduledTaskSpec spec, string userId, DateOnly startDate)
    {
        var midnight = startDate.ToDateTime(TimeOnly.MinValue);
        switch (spec.TriggerKind)
        {
            case TaskTriggerKind.Interval:
                var interval = $"PT{spec.IntervalHours.ToString(CultureInfo.InvariantCulture)}H";
                return new XElement(
                    Ns + "TimeTrigger",
                    new XElement(Ns + "StartBoundary", Format(midnight)),
                    new XElement(
                        Ns + "Repetition",
                        new XElement(Ns + "Interval", interval),
                        new XElement(Ns + "StopAtDurationEnd", "false")));

            case TaskTriggerKind.Logon:
                return new XElement(Ns + "LogonTrigger", new XElement(Ns + "UserId", userId));

            case TaskTriggerKind.Daily:
                var start = startDate.ToDateTime(spec.DailyTime);
                return new XElement(
                    Ns + "CalendarTrigger",
                    new XElement(Ns + "StartBoundary", Format(start)),
                    new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", "1")));

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.TriggerKind, "Unknown trigger kind.");
        }
    }

    private static string Format(DateTime localTime) => localTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static bool HasTrigger(XElement triggers, ScheduledTaskSpec spec, string userId)
    {
        switch (spec.TriggerKind)
        {
            case TaskTriggerKind.Interval:
                var expected = TimeSpan.FromHours(spec.IntervalHours);
                foreach (var trigger in triggers.Elements(Ns + "TimeTrigger"))
                {
                    var interval = trigger.Element(Ns + "Repetition")?.Element(Ns + "Interval")?.Value;
                    if (interval is not null && IsDuration(interval, expected))
                    {
                        return true;
                    }
                }

                return false;

            case TaskTriggerKind.Logon:
                foreach (var trigger in triggers.Elements(Ns + "LogonTrigger"))
                {
                    if (string.Equals(ChildText(trigger, "UserId"), userId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;

            case TaskTriggerKind.Daily:
                var startTime = "T" + spec.DailyTime.ToString("HH:mm", CultureInfo.InvariantCulture) + ":";
                foreach (var trigger in triggers.Elements(Ns + "CalendarTrigger"))
                {
                    var isDaily = trigger.Element(Ns + "ScheduleByDay") is not null;
                    if (isDaily && ChildText(trigger, "StartBoundary").Contains(startTime, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    // Task Scheduler may leave a setting out when it holds the default, so a missing element reads
    // as the schema default: on-battery starts disallowed, stops on unplug, no catch-up runs.
    private static bool HasLaptopSettings(XElement settings)
    {
        var startsOnBattery = !Flag(settings, "DisallowStartIfOnBatteries", defaultValue: true);
        var keepsRunningOnBattery = !Flag(settings, "StopIfGoingOnBatteries", defaultValue: true);
        var catchesUp = Flag(settings, "StartWhenAvailable", defaultValue: false);
        return startsOnBattery && keepsRunningOnBattery && catchesUp;
    }

    private static bool Flag(XElement settings, string localName, bool defaultValue)
    {
        var text = settings.Element(Ns + localName)?.Value.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return defaultValue;
        }

        return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsDuration(string value, TimeSpan expected)
    {
        try
        {
            return XmlConvert.ToTimeSpan(value.Trim()) == expected;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ChildText(XElement parent, string localName) =>
        parent.Element(Ns + localName)?.Value.Trim() ?? "";
}
