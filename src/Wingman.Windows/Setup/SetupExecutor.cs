using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Wingman.Core.Setup;
using Wingman.Core.Winget;

namespace Wingman.Windows.Setup;

/// <summary>
/// Applies a <see cref="SetupPlan"/> to the current user's Task Scheduler, HKCU, and Start Menu.
/// Each part is read before it is written, so a second run reports everything unchanged, and each
/// is applied on its own, so one failure does not stop the rest. What to do with a part once it
/// has been read is <see cref="SetupOutcomes.Decide"/>'s call.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SetupExecutor : ISetupExecutor
{
    // Removal deletes this whole key rather than the planned values one by one, so no empty
    // shell\open\command keys are left behind.
    private const string ProtocolRootKey = @"Software\Classes\wingman";

    private readonly IProcessRunner _runner;
    private readonly string _startMenuDirectory;
    private readonly string _schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    // The account the tasks run as and whose logon starts the logon task; naming it keeps the
    // logon trigger to this user, which needs no administrator rights.
    private readonly string _userId = Environment.UserDomainName + "\\" + Environment.UserName;

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

        results.Add(ApplyRegistryValue(plan.StartupEntry, items.Dequeue(), remove, dryRun));
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
                results.Add(ApplyRegistryValue(plan.ProtocolValues[i], protocolItems[i], remove: false, dryRun));
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
            var query = await _runner.RunAsync(_schtasks, ["/Query", "/TN", task.Name, "/XML"], ct);
            var exists = query.ExitCode == 0;
            var upToDate = exists && TaskDefinitionXml.Matches(query.StandardOutput, task, _userId);

            var outcome = SetupOutcomes.Decide(exists, task.Enabled, remove, dryRun, upToDate);
            if (outcome == SetupResult.Removed)
            {
                var delete = await _runner.RunAsync(_schtasks, task.SchtasksDeleteArgs, ct);
                return delete.ExitCode == 0 ? Outcome(item, outcome) : Failed(item, FirstLine(delete));
            }

            if (outcome is SetupResult.Created or SetupResult.Updated)
            {
                var register = await RegisterAsync(task, ct);
                return register.ExitCode == 0 ? Outcome(item, outcome) : Failed(item, FirstLine(register));
            }

            return Outcome(item, outcome);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failed(item, ex.Message);
        }
    }

    private async Task<ProcessResult> RegisterAsync(ScheduledTaskSpec task, CancellationToken ct)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"wingman-task-{Guid.NewGuid():N}.xml");

        // schtasks /XML reads the file as UTF-16 LE; Encoding.Unicode writes the BOM it looks for.
        await File.WriteAllTextAsync(xmlPath, TaskDefinitionXml.Build(task, _userId), Encoding.Unicode, ct);
        try
        {
            return await _runner.RunAsync(_schtasks, task.SchtasksRegisterArgs(xmlPath), ct);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static SetupResult ApplyRegistryValue(RegistryValueSpec spec, SetupItem item, bool remove, bool dryRun)
    {
        try
        {
            object? current;
            using (var key = Registry.CurrentUser.OpenSubKey(spec.KeyPath))
            {
                current = key?.GetValue(spec.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }

            var upToDate = current is string text && text == spec.Value;
            var outcome = SetupOutcomes.Decide(current is not null, spec.Enabled, remove, dryRun, upToDate);
            if (outcome == SetupResult.Removed)
            {
                using var writableKey = Registry.CurrentUser.OpenSubKey(spec.KeyPath, writable: true);
                writableKey?.DeleteValue(spec.ValueName, throwOnMissingValue: false);
            }
            else if (outcome is SetupResult.Created or SetupResult.Updated)
            {
                using var writableKey = Registry.CurrentUser.CreateSubKey(spec.KeyPath);
                writableKey.SetValue(spec.ValueName, spec.Value, RegistryValueKind.String);
            }

            return Outcome(item, outcome);
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

            for (var i = 0; i < items.Count; i++)
            {
                var outcome = SetupOutcomes.Decide(existed[i], values[i].Enabled, remove: true, dryRun, upToDate: false);
                results.Add(Outcome(items[i], outcome));
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
            var upToDate = exists && ShellLink.TryRead(path) is { } current && Matches(current, spec);

            // The shortcut carries the toast AppUserModelID, so no setting turns it off.
            var outcome = SetupOutcomes.Decide(exists, enabled: true, remove, dryRun, upToDate);
            if (outcome == SetupResult.Removed)
            {
                File.Delete(path);
            }
            else if (outcome is SetupResult.Created or SetupResult.Updated)
            {
                Directory.CreateDirectory(_startMenuDirectory);
                ShellLink.Save(path, spec);
            }

            return Outcome(item, outcome);
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
