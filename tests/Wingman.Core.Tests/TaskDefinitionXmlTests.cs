using System.Xml.Linq;
using Wingman.Core.Settings;
using Wingman.Core.Setup;

namespace Wingman.Core.Tests;

public class TaskDefinitionXmlTests
{
    private const string ExePath = @"C:\Program Files\Wingman\wingman.exe";
    private const string SystemDirectory = @"C:\Windows\System32";
    private const string UserId = @"CONTOSO\ben";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly DateOnly Today = new(2026, 9, 23);

    [Theory]
    [InlineData(1, "PT1H")]
    [InlineData(6, "PT6H")]
    [InlineData(24, "PT24H")]
    [InlineData(168, "PT168H")]
    public void Build_IntervalTask_RepeatsEveryIntervalFromMidnightToday(int hours, string expected)
    {
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = hours }, @"Wingman\Check");

        var task = Parse(TaskDefinitionXml.Build(spec, UserId, Today));

        var trigger = Assert.Single(task.Element(Ns + "Triggers")!.Elements());
        Assert.Equal(Ns + "TimeTrigger", trigger.Name);
        Assert.Equal("2026-09-23T00:00:00", trigger.Element(Ns + "StartBoundary")!.Value);
        var repetition = trigger.Element(Ns + "Repetition")!;
        Assert.Equal(expected, repetition.Element(Ns + "Interval")!.Value);
        Assert.Equal("false", repetition.Element(Ns + "StopAtDurationEnd")!.Value);
    }

    [Fact]
    public void Build_LogonTask_TriggersOnlyForTheGivenUser()
    {
        var spec = SpecFor(new WingmanSettings { CheckAtLogin = true }, @"Wingman\CheckAtLogon");

        var task = Parse(TaskDefinitionXml.Build(spec, UserId, Today));

        var trigger = Assert.Single(task.Element(Ns + "Triggers")!.Elements());
        Assert.Equal(Ns + "LogonTrigger", trigger.Name);
        Assert.Equal(UserId, trigger.Element(Ns + "UserId")!.Value);
    }

    [Fact]
    public void Build_DailyTask_StartsTodayAtTheConfiguredTime()
    {
        var spec = SpecFor(new WingmanSettings { AutoInstall = true, AutoInstallTime = "14:30" }, @"Wingman\AutoInstall");

        var task = Parse(TaskDefinitionXml.Build(spec, UserId, Today));

        var trigger = Assert.Single(task.Element(Ns + "Triggers")!.Elements());
        Assert.Equal(Ns + "CalendarTrigger", trigger.Name);
        Assert.Equal("2026-09-23T14:30:00", trigger.Element(Ns + "StartBoundary")!.Value);
        Assert.Equal("1", trigger.Element(Ns + "ScheduleByDay")!.Element(Ns + "DaysInterval")!.Value);
    }

    [Fact]
    public void Build_Document_IsATaskSchedulerTaskVersion14()
    {
        var xml = TaskDefinitionXml.Build(SpecFor(new WingmanSettings(), @"Wingman\Check"), UserId, Today);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", xml, StringComparison.Ordinal);
        var task = Parse(xml);
        Assert.Equal(Ns + "Task", task.Name);
        Assert.Equal("1.4", task.Attribute("version")!.Value);
        Assert.All(task.DescendantsAndSelf(), element => Assert.Equal(Ns, element.Name.Namespace));
        Assert.False(string.IsNullOrWhiteSpace(task.Element(Ns + "RegistrationInfo")!.Element(Ns + "Description")!.Value));
    }

    [Fact]
    public void Build_Principal_RunsAsTheUserWithLeastPrivilege()
    {
        var task = Parse(TaskDefinitionXml.Build(SpecFor(new WingmanSettings(), @"Wingman\Check"), UserId, Today));

        var principal = Assert.Single(task.Element(Ns + "Principals")!.Elements());
        Assert.Equal("Author", principal.Attribute("id")!.Value);
        Assert.Equal(UserId, principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")!.Value);
    }

    [Theory]
    [InlineData("MultipleInstancesPolicy", "IgnoreNew")]
    [InlineData("DisallowStartIfOnBatteries", "false")]
    [InlineData("StopIfGoingOnBatteries", "false")]
    [InlineData("AllowHardTerminate", "true")]
    [InlineData("StartWhenAvailable", "true")]
    [InlineData("RunOnlyIfNetworkAvailable", "true")]
    [InlineData("Enabled", "true")]
    [InlineData("Hidden", "false")]
    [InlineData("ExecutionTimeLimit", "PT2H")]
    [InlineData("Priority", "7")]
    public void Build_Settings_RunOnBatteryAndCatchUp(string setting, string expected)
    {
        var task = Parse(TaskDefinitionXml.Build(SpecFor(new WingmanSettings(), @"Wingman\Check"), UserId, Today));

        Assert.Equal(expected, task.Element(Ns + "Settings")!.Element(Ns + setting)!.Value);
    }

    [Fact]
    public void Build_Action_StartsHeadlessConhostWithTheWingmanCommand()
    {
        var task = Parse(TaskDefinitionXml.Build(SpecFor(new WingmanSettings(), @"Wingman\Check"), UserId, Today));

        var actions = task.Element(Ns + "Actions")!;
        Assert.Equal("Author", actions.Attribute("Context")!.Value);
        var exec = Assert.Single(actions.Elements());
        Assert.Equal(Ns + "Exec", exec.Name);
        Assert.Equal(@"C:\Windows\System32\conhost.exe", exec.Element(Ns + "Command")!.Value);
        Assert.Equal($"--headless \"{ExePath}\" check --notify", exec.Element(Ns + "Arguments")!.Value);
    }

    [Theory]
    [InlineData(@"Wingman\Check")]
    [InlineData(@"Wingman\CheckAtLogon")]
    [InlineData(@"Wingman\AutoInstall")]
    public void Matches_TheDocumentBuildWrote_IsTrue(string name)
    {
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = 24, AutoInstall = true }, name);

        Assert.True(TaskDefinitionXml.Matches(TaskDefinitionXml.Build(spec, UserId, Today), spec, UserId));
    }

    [Fact]
    public void Matches_WhatSchtasksQueryPrints_IgnoresRegistrationDetailsAndStartDate()
    {
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = 24 }, @"Wingman\Check");

        Assert.True(TaskDefinitionXml.Matches(RegisteredCheckTask("PT24H"), spec, UserId));
    }

    [Fact]
    public void Matches_ADifferentInterval_IsFalse()
    {
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = 24 }, @"Wingman\Check");

        Assert.False(TaskDefinitionXml.Matches(RegisteredCheckTask("PT6H"), spec, UserId));
    }

    [Fact]
    public void Matches_ALogonTaskForAnotherUser_IsFalse()
    {
        var spec = SpecFor(new WingmanSettings(), @"Wingman\CheckAtLogon");
        var registered = TaskDefinitionXml.Build(spec, @"CONTOSO\someone", Today);

        Assert.False(TaskDefinitionXml.Matches(registered, spec, UserId));
    }

    [Fact]
    public void Matches_ADailyTaskAtAnotherTime_IsFalse()
    {
        var registered = SpecFor(new WingmanSettings { AutoInstall = true, AutoInstallTime = "03:00" }, @"Wingman\AutoInstall");
        var spec = SpecFor(new WingmanSettings { AutoInstall = true, AutoInstallTime = "14:30" }, @"Wingman\AutoInstall");

        Assert.False(TaskDefinitionXml.Matches(TaskDefinitionXml.Build(registered, UserId, Today), spec, UserId));
    }

    [Fact]
    public void Matches_ATaskFromTheOldSchtasksFlags_IsFalse()
    {
        // schtasks /SC HOURLY leaves the battery defaults on and catch-up off, so the task is
        // rewritten even though its command and interval already match.
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = 6 }, @"Wingman\Check");
        var registered = RegisteredCheckTask("PT6H")
            .Replace("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", "")
            .Replace("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", "")
            .Replace("<StartWhenAvailable>true</StartWhenAvailable>", "");

        Assert.False(TaskDefinitionXml.Matches(registered, spec, UserId));
    }

    [Fact]
    public void Matches_ADifferentCommand_IsFalse()
    {
        var spec = SpecFor(new WingmanSettings { CheckIntervalHours = 24 }, @"Wingman\Check");
        var registered = RegisteredCheckTask("PT24H").Replace("check --notify", "tray");

        Assert.False(TaskDefinitionXml.Matches(registered, spec, UserId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ERROR: The system cannot find the file specified.")]
    [InlineData("<Task><Unclosed>")]
    public void Matches_OutputThatIsNotATask_IsFalse(string queryOutput)
    {
        var spec = SpecFor(new WingmanSettings(), @"Wingman\Check");

        Assert.False(TaskDefinitionXml.Matches(queryOutput, spec, UserId));
    }

    private static ScheduledTaskSpec SpecFor(WingmanSettings settings, string name) =>
        Assert.Single(SetupPlanner.Build(settings, ExePath, SystemDirectory).Tasks, task => task.Name == name);

    private static XElement Parse(string xml) => XDocument.Parse(xml).Root!;

    // The shape schtasks /Query /XML prints: its own Date, Author, and URI, and the start boundary
    // of the day the task was registered.
    private static string RegisteredCheckTask(string interval) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Date>2026-01-05T09:12:44</Date>
            <Author>CONTOSO\ben</Author>
            <Description>Checks winget for package updates and shows a toast when there are some.</Description>
            <URI>\Wingman\Check</URI>
          </RegistrationInfo>
          <Triggers>
            <TimeTrigger>
              <Repetition>
                <Interval>{interval}</Interval>
                <StopAtDurationEnd>false</StopAtDurationEnd>
              </Repetition>
              <StartBoundary>2026-01-05T00:00:00</StartBoundary>
              <Enabled>true</Enabled>
            </TimeTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>S-1-5-21-1111111111-2222222222-3333333333-1001</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>C:\Windows\System32\conhost.exe</Command>
              <Arguments>--headless "{ExePath}" check --notify</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
