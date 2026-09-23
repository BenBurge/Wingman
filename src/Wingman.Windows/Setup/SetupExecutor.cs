using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Wingman.Core.Setup;
using Wingman.Core.Winget;

namespace Wingman.Windows.Setup;

/// <summary>
/// Applies a <see cref="SetupPlan"/> to the current user's Task Scheduler, HKCU, and Start Menu.
/// Each part is read before it is written, so a second run reports everything unchanged, and each
/// is applied on its own, so one failure does not stop the rest.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SetupExecutor : ISetupExecutor
{
    private const string Schtasks = "schtasks.exe";

    // Removal deletes this whole key rather than the planned values one by one, so no empty
    // shell\open\command keys are left behind.
    private const string ProtocolRootKey = @"Software\Classes\wingman";

    private readonly IProcessRunner _runner;
    private readonly string _startMenuDirectory;

    /// <param name="startMenuDirectory">
    /// Where the shortcut goes; defaults to the user's Start Menu Programs folder. A test points
    /// it at a temporary folder so it never touches the real Start Menu.
    /// </param>
    public SetupExecutor(IProcessRunner runner, string? startMenuDirectory = null)
    {
        _runner = runner;
        _startMenuDirectory = startMenuDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    }

    public async Task<IReadOnlyList<SetupResult>> ApplyAsync(SetupPlan plan, bool remove, bool dryRun, CancellationToken ct)
    {
        // Describe lists the plan's parts in the order they are applied below.
        var items = new Queue<SetupItem>(SetupPlanner.Describe(plan));
        var results = new List<SetupResult>();

        foreach (var task in plan.Tasks)
        {
            results.Add(await ApplyTaskAsync(task, items.Dequeue(), remove, dryRun, ct));
        }

        if (plan.StartupEntry is { } startupEntry)
        {
            var item = items.Dequeue();
            results.Add(remove ? RemoveRegistryValue(startupEntry, item, dryRun) : SetRegistryValue(startupEntry, item, dryRun));
        }

        results.Add(ApplyShortcut(plan.Shortcut, items.Dequeue(), remove, dryRun));

        var protocolItems = items.ToList();
        if (remove)
        {
            results.AddRange(RemoveProtocol(plan.ProtocolValues, protocolItems, dryRun));
        }
        else
        {
            for (var i = 0; i < plan.ProtocolValues.Count; i++)
            {
                results.Add(SetRegistryValue(plan.ProtocolValues[i], protocolItems[i], dryRun));
            }
        }

        return results;
    }

    private async Task<SetupResult> ApplyTaskAsync(
        ScheduledTaskSpec task, SetupItem item, bool remove, bool dryRun, CancellationToken ct)
    {
        try
        {
            // schtasks exits non-zero for a missing task and for every other query error alike, so
            // any failure here counts as "not registered" and the create or delete reports the rest.
            var query = await _runner.RunAsync(Schtasks, ["/Query", "/TN", task.Name, "/XML"], ct);
            var exists = query.ExitCode == 0;

            if (remove)
            {
                if (!exists)
                {
                    return Outcome(item, SetupResult.Unchanged);
                }

                if (dryRun)
                {
                    return Outcome(item, SetupResult.WouldRemove);
                }

                var delete = await _runner.RunAsync(Schtasks, task.SchtasksDeleteArgs, ct);
                return delete.ExitCode == 0 ? Outcome(item, SetupResult.Removed) : Failed(item, FirstLine(delete));
            }

            if (exists && ScheduledTaskXml.Matches(query.StandardOutput, task))
            {
                return Outcome(item, SetupResult.Unchanged);
            }

            if (dryRun)
            {
                return Outcome(item, SetupResult.WouldCreate);
            }

            var create = await _runner.RunAsync(Schtasks, task.SchtasksCreateArgs, ct);
            if (create.ExitCode != 0)
            {
                return Failed(item, FirstLine(create));
            }

            return Outcome(item, exists ? SetupResult.Updated : SetupResult.Created);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Failed(item, ex.Message);
        }
    }

    private static SetupResult SetRegistryValue(RegistryValueSpec spec, SetupItem item, bool dryRun)
    {
        try
        {
            object? current;
            using (var key = Registry.CurrentUser.OpenSubKey(spec.KeyPath))
            {
                current = key?.GetValue(spec.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }

            if (current is string text && text == spec.Value)
            {
                return Outcome(item, SetupResult.Unchanged);
            }

            if (dryRun)
            {
                return Outcome(item, SetupResult.WouldCreate);
            }

            using var writableKey = Registry.CurrentUser.CreateSubKey(spec.KeyPath);
            writableKey.SetValue(spec.ValueName, spec.Value, RegistryValueKind.String);
            return Outcome(item, current is null ? SetupResult.Created : SetupResult.Updated);
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            return Failed(item, ex.Message);
        }
    }

    private static SetupResult RemoveRegistryValue(RegistryValueSpec spec, SetupItem item, bool dryRun)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(spec.KeyPath, writable: !dryRun);
            if (key?.GetValue(spec.ValueName) is null)
            {
                return Outcome(item, SetupResult.Unchanged);
            }

            if (dryRun)
            {
                return Outcome(item, SetupResult.WouldRemove);
            }

            key.DeleteValue(spec.ValueName, throwOnMissingValue: false);
            return Outcome(item, SetupResult.Removed);
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            return Failed(item, ex.Message);
        }
    }

    private static List<SetupResult> RemoveProtocol(
        IReadOnlyList<RegistryValueSpec> values, IReadOnlyList<SetupItem> items, bool dryRun)
    {
        var results = new List<SetupResult>();
        try
        {
            var existed = new List<bool>();
            foreach (var value in values)
            {
                using var key = Registry.CurrentUser.OpenSubKey(value.KeyPath);
                existed.Add(key?.GetValue(value.ValueName) is not null);
            }

            if (!dryRun)
            {
                Registry.CurrentUser.DeleteSubKeyTree(ProtocolRootKey, throwOnMissingSubKey: false);
            }

            var removedOutcome = dryRun ? SetupResult.WouldRemove : SetupResult.Removed;
            for (var i = 0; i < items.Count; i++)
            {
                results.Add(Outcome(items[i], existed[i] ? removedOutcome : SetupResult.Unchanged));
            }
        }
        catch (Exception ex) when (IsRegistryError(ex))
        {
            results.Clear();
            foreach (var item in items)
            {
                results.Add(Failed(item, ex.Message));
            }
        }

        return results;
    }

    private SetupResult ApplyShortcut(ShortcutSpec spec, SetupItem item, bool remove, bool dryRun)
    {
        var path = Path.Combine(_startMenuDirectory, spec.LinkName);
        try
        {
            var exists = File.Exists(path);

            if (remove)
            {
                if (!exists)
                {
                    return Outcome(item, SetupResult.Unchanged);
                }

                if (dryRun)
                {
                    return Outcome(item, SetupResult.WouldRemove);
                }

                File.Delete(path);
                return Outcome(item, SetupResult.Removed);
            }

            if (exists && ShellLink.TryRead(path) is { } current && Matches(current, spec))
            {
                return Outcome(item, SetupResult.Unchanged);
            }

            if (dryRun)
            {
                return Outcome(item, SetupResult.WouldCreate);
            }

            Directory.CreateDirectory(_startMenuDirectory);
            ShellLink.Save(path, spec);
            return Outcome(item, exists ? SetupResult.Updated : SetupResult.Created);
        }
        catch (Exception ex) when (ShellLink.IsShellLinkError(ex))
        {
            return Failed(item, ex.Message);
        }
    }

    private static bool Matches(ShellLinkInfo current, ShortcutSpec spec) =>
        string.Equals(current.TargetPath, spec.TargetPath, StringComparison.OrdinalIgnoreCase)
        && current.Arguments == spec.Arguments
        && current.AppUserModelId == spec.AppUserModelId;

    private static bool IsRegistryError(Exception ex) =>
        ex is SecurityException or IOException or UnauthorizedAccessException;

    private static string FirstLine(ProcessResult result)
    {
        var line = FirstNonBlankLine(result.StandardError) ?? FirstNonBlankLine(result.StandardOutput);
        return line ?? $"exit code {result.ExitCode}";
    }

    private static string? FirstNonBlankLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    private static SetupResult Outcome(SetupItem item, string outcome) => new(item, outcome, null);

    private static SetupResult Failed(SetupItem item, string error) => new(item, SetupResult.Failed, error);
}
