using System.Globalization;
using Wingman.Core.Settings;

namespace Wingman.Core.Setup;

/// <summary>
/// Builds the pure description of what <c>wingman setup</c> should register from the user's
/// settings. Nothing here touches the registry, Task Scheduler, or the filesystem; an
/// <see cref="ISetupExecutor"/> executes the plan this produces.
/// </summary>
public static class SetupPlanner
{
    private const string CheckTaskName = @"Wingman\Check";
    private const string CheckAtLogonTaskName = @"Wingman\CheckAtLogon";
    private const string AutoInstallTaskName = @"Wingman\AutoInstall";
    private const string DisabledSuffix = " (off)";
    private const string HeadlessPrefix = "conhost.exe --headless ";

    public static SetupPlan Build(WingmanSettings settings, string exePath)
    {
        var quotedExe = $"\"{exePath}\"";

        var tasks = new List<ScheduledTaskSpec>
        {
            BuildCheckTask(settings, quotedExe),
            BuildCheckAtLogonTask(quotedExe, enabled: settings.CheckAtLogin),
            BuildAutoInstallTask(settings, quotedExe, enabled: settings.AutoInstall),
        };

        var startsTrayAtLogin = settings.StartTrayAtLogin && settings.ShowTrayIcon;
        var startupEntry = new RegistryValueSpec(
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Wingman", RunHeadless($"{quotedExe} tray"), startsTrayAtLogin);

        var shortcut = new ShortcutSpec(
            "Wingman.lnk", exePath, "", "BenBurge.Wingman", "Wingman, a terminal UI for winget");

        return new SetupPlan(tasks, startupEntry, shortcut, BuildProtocolValues(quotedExe));
    }

    public static IReadOnlyList<SetupItem> Describe(SetupPlan plan)
    {
        var items = new List<SetupItem>();

        foreach (var task in plan.Tasks)
        {
            items.Add(new SetupItem("task", task.Name, DescribeTask(task) + OffSuffix(task.Enabled)));
        }

        var startupEntry = plan.StartupEntry;
        items.Add(new SetupItem(
            "registry",
            "Startup entry",
            $"Starts the tray at login: {startupEntry.KeyPath}\\{startupEntry.ValueName} = {StripHeadlessPrefix(startupEntry.Value)}"
                + OffSuffix(startupEntry.Enabled)));

        items.Add(new SetupItem(
            "shortcut",
            plan.Shortcut.LinkName,
            $"Start Menu shortcut '{plan.Shortcut.LinkName}' -> {plan.Shortcut.TargetPath} (AppUserModelID {plan.Shortcut.AppUserModelId})"));

        foreach (var value in plan.ProtocolValues)
        {
            var valueName = value.ValueName.Length == 0 ? "(Default)" : value.ValueName;
            items.Add(new SetupItem(
                "protocol",
                $"{value.KeyPath}\\{valueName}",
                $"Registers the wingman: protocol: {value.KeyPath}\\{valueName} = {value.Value}"));
        }

        return items;
    }

    private static string OffSuffix(bool enabled) => enabled ? "" : DisabledSuffix;

    // conhost.exe --headless (Windows 10 1809 and later) gives the process a console without a
    // window, so wingman.exe - a console executable - leaves nothing on the desktop when Task
    // Scheduler or the Run key launches it.
    private static string RunHeadless(string command) => HeadlessPrefix + command;

    private static string StripHeadlessPrefix(string value) =>
        value.StartsWith(HeadlessPrefix, StringComparison.Ordinal) ? value[HeadlessPrefix.Length..] : value;

    private static ScheduledTaskSpec BuildCheckTask(WingmanSettings settings, string quotedExe)
    {
        var command = RunHeadless($"{quotedExe} check --notify");
        var createArgs = new[]
        {
            "/Create", "/TN", CheckTaskName, "/TR", command,
            "/SC", "HOURLY", "/MO", settings.CheckIntervalHours.ToString(CultureInfo.InvariantCulture),
            "/F", "/RL", "LIMITED",
        };
        var deleteArgs = new[] { "/Delete", "/TN", CheckTaskName, "/F" };

        return new ScheduledTaskSpec(CheckTaskName, createArgs, deleteArgs, command, Enabled: true);
    }

    // schtasks cannot combine an interval trigger with a logon trigger in one command line, so
    // "check at login" is a second, honest task rather than one task with two triggers.
    private static ScheduledTaskSpec BuildCheckAtLogonTask(string quotedExe, bool enabled)
    {
        var command = RunHeadless($"{quotedExe} check --notify");
        var createArgs = new[]
        {
            "/Create", "/TN", CheckAtLogonTaskName, "/TR", command,
            "/SC", "ONLOGON", "/F", "/RL", "LIMITED",
        };
        var deleteArgs = new[] { "/Delete", "/TN", CheckAtLogonTaskName, "/F" };

        return new ScheduledTaskSpec(CheckAtLogonTaskName, createArgs, deleteArgs, command, enabled);
    }

    private static ScheduledTaskSpec BuildAutoInstallTask(WingmanSettings settings, string quotedExe, bool enabled)
    {
        var command = RunHeadless($"{quotedExe} upgrade --all --yes --auto --notify");
        var time = settings.AutoInstallTimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        var createArgs = new[]
        {
            "/Create", "/TN", AutoInstallTaskName, "/TR", command,
            "/SC", "DAILY", "/ST", time, "/F", "/RL", "LIMITED",
        };
        var deleteArgs = new[] { "/Delete", "/TN", AutoInstallTaskName, "/F" };

        return new ScheduledTaskSpec(AutoInstallTaskName, createArgs, deleteArgs, command, enabled);
    }

    private static IReadOnlyList<RegistryValueSpec> BuildProtocolValues(string quotedExe) =>
    [
        new(@"Software\Classes\wingman", "", "URL:Wingman Protocol", Enabled: true),
        new(@"Software\Classes\wingman", "URL Protocol", "", Enabled: true),
        new(@"Software\Classes\wingman\shell\open\command", "", $"{quotedExe} open \"%1\"", Enabled: true),
    ];

    private static string DescribeTask(ScheduledTaskSpec task)
    {
        var schedule = ArgAfter(task.SchtasksCreateArgs, "/SC");
        var detail = schedule switch
        {
            "HOURLY" => $"every {ArgAfter(task.SchtasksCreateArgs, "/MO")} hour(s)",
            "ONLOGON" => "at login",
            "DAILY" => $"daily at {ArgAfter(task.SchtasksCreateArgs, "/ST")}",
            _ => schedule,
        };

        return $"Runs {StripHeadlessPrefix(task.Command)} {detail}";
    }

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
