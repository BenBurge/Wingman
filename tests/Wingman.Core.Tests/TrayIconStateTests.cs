using Wingman.Core.Settings;
using Wingman.Core.State;
using Wingman.Core.Tray;

namespace Wingman.Core.Tests;

public class TrayIconStateTests
{
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    [Fact]
    public void From_DefaultState_IsNone()
    {
        Assert.Equal(TrayBadge.None, TrayIconState.From(new WingmanState(), new WingmanSettings()));
    }

    [Fact]
    public void From_UpdatesAvailable_IsUpdates()
    {
        var state = new WingmanState { UpdatesAvailable = 3 };

        Assert.Equal(TrayBadge.Updates, TrayIconState.From(state, new WingmanSettings()));
    }

    [Fact]
    public void From_FailedBatch_BeatsUpdates()
    {
        var state = new WingmanState { UpdatesAvailable = 3, LastBatchFailed = true };

        Assert.Equal(TrayBadge.Failed, TrayIconState.From(state, new WingmanSettings()));
    }

    [Fact]
    public void From_LastError_IsFailed()
    {
        var state = new WingmanState { LastError = "winget timed out" };

        Assert.Equal(TrayBadge.Failed, TrayIconState.From(state, new WingmanSettings()));
    }

    [Fact]
    public void From_Running_BeatsFailedAndUpdates()
    {
        var state = new WingmanState { UpdatesAvailable = 3, LastBatchFailed = true, Running = true };

        Assert.Equal(TrayBadge.Working, TrayIconState.From(state, new WingmanSettings()));
    }

    [Fact]
    public void From_NotificationsPaused_BeatsEverything()
    {
        var state = new WingmanState { UpdatesAvailable = 3, LastBatchFailed = true, Running = true };
        var settings = new WingmanSettings { NotificationsPaused = true };

        Assert.Equal(TrayBadge.Paused, TrayIconState.From(state, settings));
    }

    [Theory]
    [InlineData(TrayBadge.None, false, "wingman-tray.ico")]
    [InlineData(TrayBadge.None, true, "wingman-tray-dark.ico")]
    [InlineData(TrayBadge.Updates, false, "wingman-tray-updates.ico")]
    [InlineData(TrayBadge.Updates, true, "wingman-tray-updates.ico")]
    [InlineData(TrayBadge.Working, false, "wingman-tray-working.ico")]
    [InlineData(TrayBadge.Failed, false, "wingman-tray-failed.ico")]
    [InlineData(TrayBadge.Paused, true, "wingman-tray-paused.ico")]
    public void IconFileName_MapsBadgeAndTaskbarTheme(TrayBadge badge, bool lightTaskbar, string expected)
    {
        Assert.Equal(expected, TrayIconState.IconFileName(badge, lightTaskbar));
    }

    [Fact]
    public void IconFileName_EveryBadgeNamesAShippedIcon()
    {
        var trayDirectory = Path.Combine(RepoRoot(), "assets", "tray");

        foreach (var badge in Enum.GetValues<TrayBadge>())
        {
            Assert.True(File.Exists(Path.Combine(trayDirectory, TrayIconState.IconFileName(badge, false))));
            Assert.True(File.Exists(Path.Combine(trayDirectory, TrayIconState.IconFileName(badge, true))));
        }
    }

    [Fact]
    public void Tooltip_Updates_CountsThem()
    {
        var state = new WingmanState { UpdatesAvailable = 3 };

        Assert.Equal("Wingman · 3 updates available", TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_OneUpdate_IsSingular()
    {
        var state = new WingmanState { UpdatesAvailable = 1 };

        Assert.Equal("Wingman · 1 update available", TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_Running_SaysWorking()
    {
        var state = new WingmanState { Running = true };

        Assert.Equal("Wingman · working…", TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_Failed_SaysLastRunFailed()
    {
        var state = new WingmanState { LastBatchFailed = true };

        Assert.Equal("Wingman · last run failed", TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_Paused_SaysNotificationsPaused()
    {
        var settings = new WingmanSettings { NotificationsPaused = true };

        Assert.Equal("Wingman · notifications paused", TrayIconState.Tooltip(new WingmanState(), settings, PlusTwo));
    }

    [Fact]
    public void Tooltip_UpToDate_ShowsLocalCheckTime()
    {
        var state = new WingmanState { LastCheck = new DateTimeOffset(2026, 9, 23, 12, 31, 0, TimeSpan.Zero) };

        Assert.Equal(
            "Wingman · up to date (checked 14:31)",
            TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_NeverChecked_SaysSo()
    {
        Assert.Equal(
            "Wingman · never checked",
            TrayIconState.Tooltip(new WingmanState(), new WingmanSettings(), PlusTwo));
    }

    [Fact]
    public void Tooltip_FitsTheShellLimit()
    {
        var state = new WingmanState { UpdatesAvailable = int.MaxValue };

        // NOTIFYICONDATAW.szTip holds 128 characters including the terminator.
        Assert.True(TrayIconState.Tooltip(state, new WingmanSettings(), PlusTwo).Length < 128);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wingman.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Wingman.sln not found above the test output.");
    }
}
