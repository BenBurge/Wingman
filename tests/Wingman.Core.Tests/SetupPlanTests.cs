using Wingman.Core.Setup;
using Wingman.Core.Settings;

namespace Wingman.Core.Tests;

public class SetupPlanTests
{
    private const string ExePath = @"C:\Program Files\Wingman\wingman.exe";

    [Fact]
    public void Build_DefaultSettings_CreatesCheckTaskWithExactArgv()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        var command = $"\"{ExePath}\" check --notify";
        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.Equal(command, checkTask.Command);
        Assert.Equal(
            new[]
            {
                "/Create", "/TN", @"Wingman\Check", "/TR", command,
                "/SC", "HOURLY", "/MO", "6", "/F", "/RL", "LIMITED",
            },
            checkTask.SchtasksCreateArgs);
        Assert.Equal(["/Delete", "/TN", @"Wingman\Check", "/F"], checkTask.SchtasksDeleteArgs);
    }

    [Fact]
    public void Build_CheckIntervalHours_LandsInCreateArgv()
    {
        var settings = new WingmanSettings { CheckIntervalHours = 12 };

        var plan = SetupPlanner.Build(settings, ExePath);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.Equal("12", ArgAfter(checkTask.SchtasksCreateArgs, "/MO"));
    }

    [Fact]
    public void Build_DefaultSettings_CheckTaskIsAlwaysEnabled()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.True(checkTask.Enabled);
    }

    [Fact]
    public void Build_CheckAtLoginTrue_EnablesOnLogonTask()
    {
        var settings = new WingmanSettings { CheckAtLogin = true };

        var plan = SetupPlanner.Build(settings, ExePath);

        var logonTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\CheckAtLogon");
        Assert.True(logonTask.Enabled);
        Assert.Equal("ONLOGON", ArgAfter(logonTask.SchtasksCreateArgs, "/SC"));
        Assert.Equal(["/Delete", "/TN", @"Wingman\CheckAtLogon", "/F"], logonTask.SchtasksDeleteArgs);
    }

    [Fact]
    public void Build_CheckAtLoginFalse_KeepsLogonTaskDisabled()
    {
        var settings = new WingmanSettings { CheckAtLogin = false };

        var plan = SetupPlanner.Build(settings, ExePath);

        var logonTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\CheckAtLogon");
        Assert.False(logonTask.Enabled);
        Assert.Equal(3, plan.Tasks.Count);
    }

    [Fact]
    public void Build_AutoInstallTrue_AddsDailyTaskAtConfiguredTime()
    {
        var settings = new WingmanSettings { AutoInstall = true, AutoInstallTime = "14:30" };

        var plan = SetupPlanner.Build(settings, ExePath);

        var autoInstallTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\AutoInstall");
        Assert.True(autoInstallTask.Enabled);
        Assert.Equal($"\"{ExePath}\" upgrade --all --yes --auto --notify", autoInstallTask.Command);
        Assert.Equal("DAILY", ArgAfter(autoInstallTask.SchtasksCreateArgs, "/SC"));
        Assert.Equal("14:30", ArgAfter(autoInstallTask.SchtasksCreateArgs, "/ST"));
        Assert.Equal(["/Delete", "/TN", @"Wingman\AutoInstall", "/F"], autoInstallTask.SchtasksDeleteArgs);
    }

    [Fact]
    public void Build_AutoInstallFalse_KeepsAutoInstallTaskDisabled()
    {
        var settings = new WingmanSettings { AutoInstall = false };

        var plan = SetupPlanner.Build(settings, ExePath);

        var autoInstallTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\AutoInstall");
        Assert.False(autoInstallTask.Enabled);
    }

    [Fact]
    public void Build_StartTrayAtLoginAndShowTrayIconTrue_EnablesStartupEntry()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = true, ShowTrayIcon = true };

        var plan = SetupPlanner.Build(settings, ExePath);

        Assert.True(plan.StartupEntry.Enabled);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", plan.StartupEntry.KeyPath);
        Assert.Equal("Wingman", plan.StartupEntry.ValueName);
        Assert.Equal($"\"{ExePath}\" tray", plan.StartupEntry.Value);
    }

    [Fact]
    public void Build_StartTrayAtLoginFalse_KeepsStartupEntryDisabled()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = false, ShowTrayIcon = true };

        var plan = SetupPlanner.Build(settings, ExePath);

        Assert.False(plan.StartupEntry.Enabled);
        Assert.Equal("Wingman", plan.StartupEntry.ValueName);
    }

    [Fact]
    public void Build_ShowTrayIconFalse_KeepsStartupEntryDisabled()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = true, ShowTrayIcon = false };

        var plan = SetupPlanner.Build(settings, ExePath);

        Assert.False(plan.StartupEntry.Enabled);
    }

    [Fact]
    public void Build_Shortcut_MatchesSpec()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        Assert.Equal("Wingman.lnk", plan.Shortcut.LinkName);
        Assert.Equal(ExePath, plan.Shortcut.TargetPath);
        Assert.Equal("", plan.Shortcut.Arguments);
        Assert.Equal("BenBurge.Wingman", plan.Shortcut.AppUserModelId);
        Assert.Equal("Wingman, a terminal UI for winget", plan.Shortcut.Description);
    }

    [Fact]
    public void Build_ProtocolValues_MatchesSpec()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        Assert.Equal(3, plan.ProtocolValues.Count);
        Assert.All(plan.ProtocolValues, value => Assert.True(value.Enabled));

        var classRoot = plan.ProtocolValues[0];
        Assert.Equal(@"Software\Classes\wingman", classRoot.KeyPath);
        Assert.Equal("", classRoot.ValueName);
        Assert.Equal("URL:Wingman Protocol", classRoot.Value);

        var urlProtocol = plan.ProtocolValues[1];
        Assert.Equal(@"Software\Classes\wingman", urlProtocol.KeyPath);
        Assert.Equal("URL Protocol", urlProtocol.ValueName);
        Assert.Equal("", urlProtocol.Value);

        var openCommand = plan.ProtocolValues[2];
        Assert.Equal(@"Software\Classes\wingman\shell\open\command", openCommand.KeyPath);
        Assert.Equal("", openCommand.ValueName);
        Assert.Equal($"\"{ExePath}\" open \"%1\"", openCommand.Value);
    }

    [Fact]
    public void Describe_HasOneLinePerItem_DefaultSettings()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        var items = SetupPlanner.Describe(plan);

        // 3 tasks (check, logon, auto-install) + 1 startup entry + 1 shortcut + 3 protocol values.
        Assert.Equal(8, items.Count);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
    }

    [Fact]
    public void Describe_DefaultSettings_MarksOnlyAutoInstallOff()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        var items = SetupPlanner.Describe(plan);

        var offItem = Assert.Single(items, item => item.Description.EndsWith(" (off)", StringComparison.Ordinal));
        Assert.Equal(@"Wingman\AutoInstall", offItem.Name);
    }

    [Fact]
    public void Describe_AutoInstallOnAndStartupEntryOff_MarksStartupEntryOff()
    {
        var settings = new WingmanSettings { AutoInstall = true, StartTrayAtLogin = false };

        var plan = SetupPlanner.Build(settings, ExePath);
        var items = SetupPlanner.Describe(plan);

        Assert.Equal(8, items.Count);
        var registryItem = Assert.Single(items, item => item.Kind == "registry");
        Assert.EndsWith(" (off)", registryItem.Description, StringComparison.Ordinal);
        var autoInstallItem = Assert.Single(items, item => item.Name == @"Wingman\AutoInstall");
        Assert.DoesNotContain("(off)", autoInstallItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TaskItems_UseTaskKindAndName()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath);

        var items = SetupPlanner.Describe(plan);

        var checkItem = Assert.Single(items, item => item.Name == @"Wingman\Check");
        Assert.Equal("task", checkItem.Kind);
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
