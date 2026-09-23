using Wingman.Core.Setup;
using Wingman.Core.Settings;

namespace Wingman.Core.Tests;

public class SetupPlanTests
{
    private const string ExePath = @"C:\Program Files\Wingman\wingman.exe";
    private const string SystemDirectory = @"C:\Windows\System32";
    private const string Conhost = @"C:\Windows\System32\conhost.exe";

    [Fact]
    public void Build_DefaultSettings_CreatesCheckTaskEverySixHours()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.Equal(TaskTriggerKind.Interval, checkTask.TriggerKind);
        Assert.Equal(6, checkTask.IntervalHours);
        Assert.Equal(Conhost, checkTask.Executable);
        Assert.Equal($"--headless \"{ExePath}\" check --notify", checkTask.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(checkTask.Description));
    }

    [Fact]
    public void Tasks_RegisterFromXmlAndDeleteByName()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.Equal(
            ["/Create", "/TN", @"Wingman\Check", "/XML", @"C:\Temp\task.xml", "/F"],
            checkTask.SchtasksRegisterArgs(@"C:\Temp\task.xml"));
        Assert.Equal(["/Delete", "/TN", @"Wingman\Check", "/F"], checkTask.SchtasksDeleteArgs);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(168)]
    public void Build_CheckIntervalHours_LandsInTheSpec(int hours)
    {
        var settings = new WingmanSettings { CheckIntervalHours = hours };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.Equal(hours, checkTask.IntervalHours);
    }

    [Fact]
    public void Build_DefaultSettings_CheckTaskIsAlwaysEnabled()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var checkTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\Check");
        Assert.True(checkTask.Enabled);
    }

    [Fact]
    public void Build_CheckAtLoginTrue_EnablesLogonTask()
    {
        var settings = new WingmanSettings { CheckAtLogin = true };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        var logonTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\CheckAtLogon");
        Assert.True(logonTask.Enabled);
        Assert.Equal(TaskTriggerKind.Logon, logonTask.TriggerKind);
        Assert.Equal($"--headless \"{ExePath}\" check --notify", logonTask.Arguments);
        Assert.Equal(["/Delete", "/TN", @"Wingman\CheckAtLogon", "/F"], logonTask.SchtasksDeleteArgs);
    }

    [Fact]
    public void Build_CheckAtLoginFalse_KeepsLogonTaskDisabled()
    {
        var settings = new WingmanSettings { CheckAtLogin = false };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        var logonTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\CheckAtLogon");
        Assert.False(logonTask.Enabled);
        Assert.Equal(3, plan.Tasks.Count);
    }

    [Fact]
    public void Build_AutoInstallTrue_AddsDailyTaskAtConfiguredTime()
    {
        var settings = new WingmanSettings { AutoInstall = true, AutoInstallTime = "14:30" };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        var autoInstallTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\AutoInstall");
        Assert.True(autoInstallTask.Enabled);
        Assert.Equal(TaskTriggerKind.Daily, autoInstallTask.TriggerKind);
        Assert.Equal(new TimeOnly(14, 30), autoInstallTask.DailyTime);
        Assert.Equal(Conhost, autoInstallTask.Executable);
        Assert.Equal($"--headless \"{ExePath}\" upgrade --all --yes --auto --notify", autoInstallTask.Arguments);
        Assert.Equal(["/Delete", "/TN", @"Wingman\AutoInstall", "/F"], autoInstallTask.SchtasksDeleteArgs);
    }

    [Fact]
    public void Build_AutoInstallFalse_KeepsAutoInstallTaskDisabled()
    {
        var settings = new WingmanSettings { AutoInstall = false };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        var autoInstallTask = Assert.Single(plan.Tasks, task => task.Name == @"Wingman\AutoInstall");
        Assert.False(autoInstallTask.Enabled);
    }

    [Fact]
    public void Build_StartTrayAtLoginAndShowTrayIconTrue_EnablesStartupEntry()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = true, ShowTrayIcon = true };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        Assert.True(plan.StartupEntry.Enabled);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", plan.StartupEntry.KeyPath);
        Assert.Equal("Wingman", plan.StartupEntry.ValueName);
        Assert.Equal($"\"{Conhost}\" --headless \"{ExePath}\" tray", plan.StartupEntry.Value);
    }

    [Fact]
    public void Build_StartTrayAtLoginFalse_KeepsStartupEntryDisabled()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = false, ShowTrayIcon = true };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        Assert.False(plan.StartupEntry.Enabled);
        Assert.Equal("Wingman", plan.StartupEntry.ValueName);
    }

    [Fact]
    public void Build_ShowTrayIconFalse_KeepsStartupEntryDisabled()
    {
        var settings = new WingmanSettings { StartTrayAtLogin = true, ShowTrayIcon = false };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        Assert.False(plan.StartupEntry.Enabled);
    }

    [Fact]
    public void Build_Shortcut_MatchesSpec()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        Assert.Equal("Wingman.lnk", plan.Shortcut.LinkName);
        Assert.Equal(ExePath, plan.Shortcut.TargetPath);
        Assert.Equal("", plan.Shortcut.Arguments);
        Assert.Equal("BenBurge.Wingman", plan.Shortcut.AppUserModelId);
        Assert.Equal("Wingman, a terminal UI for winget", plan.Shortcut.Description);
    }

    [Fact]
    public void Build_ProtocolValues_MatchesSpec()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

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
    public void Build_AllTasks_RunThroughHeadlessConhostFromSystem32()
    {
        var settings = new WingmanSettings { CheckAtLogin = true, AutoInstall = true };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);

        Assert.All(plan.Tasks, task =>
        {
            Assert.Equal(Conhost, task.Executable);
            Assert.StartsWith("--headless ", task.Arguments, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Build_SystemDirectoryWithTrailingBackslash_JoinsOnce()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory + "\\");

        Assert.Equal(Conhost, plan.Tasks[0].Executable);
    }

    [Fact]
    public void Describe_TaskLines_ShowPlainCommandWithoutHeadlessPrefix()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var items = SetupPlanner.Describe(plan);

        var checkItem = Assert.Single(items, item => item.Name == @"Wingman\Check");
        Assert.Equal($"Runs \"{ExePath}\" check --notify every 6 hour(s)", checkItem.Description);
        Assert.DoesNotContain("conhost.exe", checkItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_LogonAndDailyTasks_NameTheirTrigger()
    {
        var settings = new WingmanSettings { AutoInstall = true, AutoInstallTime = "14:30" };

        var items = SetupPlanner.Describe(SetupPlanner.Build(settings, ExePath, SystemDirectory));

        var logonItem = Assert.Single(items, item => item.Name == @"Wingman\CheckAtLogon");
        Assert.EndsWith(" at login", logonItem.Description, StringComparison.Ordinal);
        var autoInstallItem = Assert.Single(items, item => item.Name == @"Wingman\AutoInstall");
        Assert.EndsWith(" daily at 14:30", autoInstallItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_StartupEntryLine_ShowsPlainCommandWithoutHeadlessPrefix()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var items = SetupPlanner.Describe(plan);

        var registryItem = Assert.Single(items, item => item.Kind == "registry");
        Assert.Contains($"= \"{ExePath}\" tray", registryItem.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("conhost.exe", registryItem.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_HasOneLinePerItem_DefaultSettings()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var items = SetupPlanner.Describe(plan);

        // 3 tasks (check, logon, auto-install) + 1 startup entry + 1 shortcut + 3 protocol values.
        Assert.Equal(8, items.Count);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
    }

    [Fact]
    public void Describe_DefaultSettings_MarksOnlyAutoInstallOff()
    {
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var items = SetupPlanner.Describe(plan);

        var offItem = Assert.Single(items, item => item.Description.EndsWith(" (off)", StringComparison.Ordinal));
        Assert.Equal(@"Wingman\AutoInstall", offItem.Name);
    }

    [Fact]
    public void Describe_AutoInstallOnAndStartupEntryOff_MarksStartupEntryOff()
    {
        var settings = new WingmanSettings { AutoInstall = true, StartTrayAtLogin = false };

        var plan = SetupPlanner.Build(settings, ExePath, SystemDirectory);
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
        var plan = SetupPlanner.Build(new WingmanSettings(), ExePath, SystemDirectory);

        var items = SetupPlanner.Describe(plan);

        var checkItem = Assert.Single(items, item => item.Name == @"Wingman\Check");
        Assert.Equal("task", checkItem.Kind);
    }
}
