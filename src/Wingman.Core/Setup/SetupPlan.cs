namespace Wingman.Core.Setup;

/// <summary>
/// One human-readable line describing part of a <see cref="SetupPlan"/>, as printed by
/// <c>wingman setup --dry-run</c>.
/// </summary>
/// <param name="Kind">One of <c>task</c>, <c>registry</c>, <c>shortcut</c>, <c>protocol</c>.</param>
public sealed record SetupItem(string Kind, string Name, string Description);

/// <summary>When a <see cref="ScheduledTaskSpec"/> runs.</summary>
public enum TaskTriggerKind
{
    /// <summary>Every <see cref="ScheduledTaskSpec.IntervalHours"/> hours, starting at midnight today.</summary>
    Interval,

    /// <summary>When the user who ran setup logs on.</summary>
    Logon,

    /// <summary>Once a day at <see cref="ScheduledTaskSpec.DailyTime"/>.</summary>
    Daily,
}

/// <summary>
/// A Task Scheduler entry <see cref="SetupPlanner"/> wants registered, or removed when
/// <paramref name="Enabled"/> is false because its setting is off. It is registered from the
/// document <see cref="TaskDefinitionXml.Build"/> writes, because the <c>schtasks</c> command-line
/// flags cannot express an interval over 23 hours, a logon trigger for one user, or the battery
/// and missed-run settings.
/// </summary>
/// <param name="Description">The task's description in Task Scheduler.</param>
/// <param name="IntervalHours">The repetition interval; used only by <see cref="TaskTriggerKind.Interval"/>.</param>
/// <param name="DailyTime">The local start time; used only by <see cref="TaskTriggerKind.Daily"/>.</param>
/// <param name="Executable">The full path of the program the task starts.</param>
/// <param name="Arguments">The program's command line, already quoted, since Task Scheduler passes it through as is.</param>
/// <param name="Enabled">
/// False when the setting behind the task is off, so applying the plan removes the task if an
/// earlier setup registered it.
/// </param>
public sealed record ScheduledTaskSpec(
    string Name,
    string Description,
    TaskTriggerKind TriggerKind,
    int IntervalHours,
    TimeOnly DailyTime,
    string Executable,
    string Arguments,
    bool Enabled)
{
    /// <summary>Argv for <c>schtasks</c> that registers the task from the definition written to <paramref name="xmlPath"/>, replacing any earlier one.</summary>
    public string[] SchtasksRegisterArgs(string xmlPath) => ["/Create", "/TN", Name, "/XML", xmlPath, "/F"];

    /// <summary>Argv for <c>schtasks</c> that deletes the task without asking.</summary>
    public string[] SchtasksDeleteArgs => ["/Delete", "/TN", Name, "/F"];
}

/// <summary>
/// An HKCU value <see cref="SetupPlanner"/> wants written. <paramref name="ValueName"/> of <c>""</c>
/// targets the key's default value. <paramref name="Enabled"/> false means the setting behind it is
/// off, so applying the plan removes the value if an earlier setup wrote it.
/// </summary>
public sealed record RegistryValueSpec(string KeyPath, string ValueName, string Value, bool Enabled);

/// <summary>A Start Menu shortcut <see cref="SetupPlanner"/> wants created, carrying the AppUserModelID that unpackaged apps need for toasts.</summary>
public sealed record ShortcutSpec(string LinkName, string TargetPath, string Arguments, string AppUserModelId, string Description);

/// <summary>
/// Everything <c>wingman setup</c> registers, built once from
/// <see cref="Wingman.Core.Settings.WingmanSettings"/> so the Windows-only side only has to
/// execute it. Every task and the startup entry are always listed, disabled ones included, so a
/// second setup after a setting was turned off cleans up what the first one registered.
/// </summary>
public sealed record SetupPlan(
    IReadOnlyList<ScheduledTaskSpec> Tasks,
    RegistryValueSpec StartupEntry,
    ShortcutSpec Shortcut,
    IReadOnlyList<RegistryValueSpec> ProtocolValues);
