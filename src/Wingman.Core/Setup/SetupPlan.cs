namespace Wingman.Core.Setup;

/// <summary>
/// One human-readable line describing part of a <see cref="SetupPlan"/>, as printed by
/// <c>wingman setup --dry-run</c>.
/// </summary>
/// <param name="Kind">One of <c>task</c>, <c>registry</c>, <c>shortcut</c>, <c>protocol</c>.</param>
public sealed record SetupItem(string Kind, string Name, string Description);

/// <summary>
/// A Task Scheduler entry <see cref="SetupPlanner"/> wants registered, or removed when
/// <paramref name="Enabled"/> is false because its setting is off.
/// </summary>
/// <param name="SchtasksCreateArgs">
/// Argv for <c>schtasks /Create</c>, as separate tokens; <see cref="Command"/> appears here
/// already quoted as the single <c>/TR</c> token, since schtasks needs the whole command
/// double-quoted and the process runner must not quote it again.
/// </param>
/// <param name="Enabled">
/// False when the setting behind the task is off, so applying the plan removes the task if an
/// earlier setup registered it.
/// </param>
public sealed record ScheduledTaskSpec(
    string Name, string[] SchtasksCreateArgs, string[] SchtasksDeleteArgs, string Command, bool Enabled);

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
