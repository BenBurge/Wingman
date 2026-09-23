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
    private const string HeadlessFlag = "--headless ";

    /// <param name="systemDirectory">
    /// The Windows System32 folder <c>conhost.exe</c> is started from; defaults to
    /// <see cref="Environment.SystemDirectory"/>. Tests pass a fixed path so the plan is the same on
    /// every OS.
    /// </param>
    public static SetupPlan Build(WingmanSettings settings, string exePath, string? systemDirectory = null)
    {
        var quotedExe = $"\"{exePath}\"";
        var conhost = SystemTool.PathIn(systemDirectory ?? Environment.SystemDirectory, "conhost.exe");

        var tasks = new List<ScheduledTaskSpec>
        {
            BuildCheckTask(settings, conhost, quotedExe),
            BuildCheckAtLogonTask(conhost, quotedExe, enabled: settings.CheckAtLogin),
            BuildAutoInstallTask(settings, conhost, quotedExe, enabled: settings.AutoInstall),
        };

        var startsTrayAtLogin = settings.StartTrayAtLogin && settings.ShowTrayIcon;
        var startupCommand = $"\"{conhost}\" {Headless($"{quotedExe} tray")}";
        var startupEntry = new RegistryValueSpec(
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Wingman", startupCommand, startsTrayAtLogin);

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
            $"Starts the tray at login: {startupEntry.KeyPath}\\{startupEntry.ValueName} = {WithoutHeadlessHost(startupEntry.Value)}"
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
    private static string Headless(string command) => HeadlessFlag + command;

    // The dry-run lines show what Wingman runs, not the conhost wrapper around it.
    private static string WithoutHeadlessHost(string commandLine)
    {
        var index = commandLine.IndexOf(HeadlessFlag, StringComparison.Ordinal);
        return index < 0 ? commandLine : commandLine[(index + HeadlessFlag.Length)..];
    }

    private static ScheduledTaskSpec BuildCheckTask(WingmanSettings settings, string conhost, string quotedExe) =>
        new(
            CheckTaskName,
            "Checks winget for package updates and shows a toast when there are some.",
            TaskTriggerKind.Interval,
            IntervalHours: settings.CheckIntervalHours,
            DailyTime: default,
            conhost,
            Headless($"{quotedExe} check --notify"),
            Enabled: true);

    // Task Scheduler could carry both triggers on one task, but a separate logon task lets the
    // "check at login" setting remove it without touching the interval check.
    private static ScheduledTaskSpec BuildCheckAtLogonTask(string conhost, string quotedExe, bool enabled) =>
        new(
            CheckAtLogonTaskName,
            "Checks winget for package updates when you log on.",
            TaskTriggerKind.Logon,
            IntervalHours: 0,
            DailyTime: default,
            conhost,
            Headless($"{quotedExe} check --notify"),
            enabled);

    private static ScheduledTaskSpec BuildAutoInstallTask(
        WingmanSettings settings, string conhost, string quotedExe, bool enabled) =>
        new(
            AutoInstallTaskName,
            "Installs the package updates whose options turn on automatic updates.",
            TaskTriggerKind.Daily,
            IntervalHours: 0,
            DailyTime: settings.AutoInstallTimeOfDay,
            conhost,
            Headless($"{quotedExe} upgrade --all --yes --auto --notify"),
            enabled);

    private static IReadOnlyList<RegistryValueSpec> BuildProtocolValues(string quotedExe) =>
    [
        new(@"Software\Classes\wingman", "", "URL:Wingman Protocol", Enabled: true),
        new(@"Software\Classes\wingman", "URL Protocol", "", Enabled: true),
        new(@"Software\Classes\wingman\shell\open\command", "", $"{quotedExe} open \"%1\"", Enabled: true),
    ];

    private static string DescribeTask(ScheduledTaskSpec task)
    {
        var detail = task.TriggerKind switch
        {
            TaskTriggerKind.Interval => $"every {task.IntervalHours.ToString(CultureInfo.InvariantCulture)} hour(s)",
            TaskTriggerKind.Logon => "at login",
            TaskTriggerKind.Daily => $"daily at {task.DailyTime.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            _ => task.TriggerKind.ToString(),
        };

        return $"Runs {WithoutHeadlessHost(task.Arguments)} {detail}";
    }
}
