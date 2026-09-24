using System.Text;
using System.Text.Json;
using Wingman.Core.Settings;

namespace Wingman.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    [Fact]
    public void Load_WithNoFile_ReturnsDefaultsAndDoesNotCreateFile()
    {
        var store = new SettingsStore(_directoryPath);

        var settings = store.Load();

        Assert.Equal("", settings.DefaultScope);
        Assert.True(settings.AcceptAgreements);
        Assert.True(settings.IncludeUnknown);
        Assert.Equal(ElevationMode.Auto, settings.ElevationMode);
        Assert.Equal(ElevationLauncher.Direct, settings.ElevationLauncher);
        Assert.True(settings.ContinueOnFailure);
        Assert.Equal("Midnight", settings.Theme);
        Assert.Equal(6, settings.CheckIntervalHours);
        Assert.True(settings.CheckAtLogin);
        Assert.False(settings.AutoInstall);
        Assert.Equal("03:00", settings.AutoInstallTime);
        Assert.Equal(new TimeOnly(3, 0), settings.AutoInstallTimeOfDay);
        Assert.True(settings.AutoUpdateWingman);
        Assert.True(settings.ToastOnUpdates);
        Assert.True(settings.ToastOnBatch);
        Assert.True(settings.ShowTrayIcon);
        Assert.True(settings.StartTrayAtLogin);
        Assert.False(settings.NotificationsPaused);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryPropertyWithNonDefaultValues()
    {
        var store = new SettingsStore(_directoryPath);
        var settings = new WingmanSettings
        {
            DefaultScope = "machine",
            AcceptAgreements = false,
            IncludeUnknown = false,
            ElevationMode = ElevationMode.Always,
            ElevationLauncher = ElevationLauncher.PowerShell,
            ContinueOnFailure = false,
            Theme = "Auto",
            CheckIntervalHours = 12,
            CheckAtLogin = false,
            AutoInstall = true,
            AutoInstallTime = "23:45",
            AutoUpdateWingman = false,
            ToastOnUpdates = false,
            ToastOnBatch = false,
            ShowTrayIcon = false,
            StartTrayAtLogin = false,
            NotificationsPaused = true,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("machine", loaded.DefaultScope);
        Assert.False(loaded.AcceptAgreements);
        Assert.False(loaded.IncludeUnknown);
        Assert.Equal(ElevationMode.Always, loaded.ElevationMode);
        Assert.Equal(ElevationLauncher.PowerShell, loaded.ElevationLauncher);
        Assert.False(loaded.ContinueOnFailure);
        Assert.Equal("Auto", loaded.Theme);
        Assert.Equal(12, loaded.CheckIntervalHours);
        Assert.False(loaded.CheckAtLogin);
        Assert.True(loaded.AutoInstall);
        Assert.Equal("23:45", loaded.AutoInstallTime);
        Assert.Equal(new TimeOnly(23, 45), loaded.AutoInstallTimeOfDay);
        Assert.False(loaded.AutoUpdateWingman);
        Assert.False(loaded.ToastOnUpdates);
        Assert.False(loaded.ToastOnBatch);
        Assert.False(loaded.ShowTrayIcon);
        Assert.False(loaded.StartTrayAtLogin);
        Assert.True(loaded.NotificationsPaused);
    }

    [Theory]
    [InlineData("25:99")]
    [InlineData("3pm")]
    [InlineData("")]
    public void Load_WithInvalidAutoInstallTime_FallsBackToDefault(string invalidTime)
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, $$"""
            {
              "autoInstallTime": {{JsonSerializer.Serialize(invalidTime)}}
            }
            """);

        var settings = store.Load();

        Assert.Equal("03:00", settings.AutoInstallTime);
        Assert.Equal(new TimeOnly(3, 0), settings.AutoInstallTimeOfDay);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(999, 168)]
    public void Load_WithOutOfRangeCheckIntervalHours_ClampsToValidRange(int stored, int expected)
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, $$"""
            {
              "checkIntervalHours": {{stored}}
            }
            """);

        var settings = store.Load();

        Assert.Equal(expected, settings.CheckIntervalHours);
    }

    [Fact]
    public void Load_WithUnknownKeysPlusOneKnownKey_LoadsThatKeyAndDefaultsTheRest()
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, """
            {
              "theme": "Daylight",
              "somethingFromAFutureVersion": true,
              "anotherUnknownKey": 42
            }
            """);

        var settings = store.Load();

        Assert.Equal("Daylight", settings.Theme);
        Assert.Equal("", settings.DefaultScope);
        Assert.True(settings.AcceptAgreements);
        Assert.True(settings.IncludeUnknown);
    }

    [Fact]
    public void Load_WithLeftoverDefaultSourceKey_IgnoresItAndLoadsDefaults()
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, """
            {
              "defaultSource": "all"
            }
            """);

        var settings = store.Load();

        Assert.Equal("", settings.DefaultScope);
        Assert.True(settings.AcceptAgreements);
        Assert.True(settings.IncludeUnknown);
        Assert.Equal(ElevationMode.Auto, settings.ElevationMode);
        Assert.True(settings.ContinueOnFailure);
        Assert.Equal("Midnight", settings.Theme);
    }

    [Theory]
    [InlineData(ElevationMode.Auto, "auto")]
    [InlineData(ElevationMode.Always, "always")]
    [InlineData(ElevationMode.Never, "never")]
    public void SaveThenLoad_RoundTripsElevationModeAsCamelCaseString(ElevationMode mode, string expectedJson)
    {
        var store = new SettingsStore(_directoryPath);

        store.Save(new WingmanSettings { ElevationMode = mode });
        var text = File.ReadAllText(store.FilePath);
        var loaded = store.Load();

        Assert.Contains($"\"elevationMode\": \"{expectedJson}\"", text);
        Assert.Equal(mode, loaded.ElevationMode);
    }

    [Theory]
    [InlineData(ElevationLauncher.Direct, "direct")]
    [InlineData(ElevationLauncher.PowerShell, "powerShell")]
    public void SaveThenLoad_RoundTripsElevationLauncherAsCamelCaseString(ElevationLauncher launcher, string expectedJson)
    {
        var store = new SettingsStore(_directoryPath);

        store.Save(new WingmanSettings { ElevationLauncher = launcher });
        var text = File.ReadAllText(store.FilePath);
        var loaded = store.Load();

        Assert.Contains($"\"elevationLauncher\": \"{expectedJson}\"", text);
        Assert.Equal(launcher, loaded.ElevationLauncher);
    }

    [Fact]
    public void Load_WithUnknownElevationMode_ReturnsDefaults()
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, """
            {
              "theme": "Daylight",
              "elevationMode": "sometimes"
            }
            """);

        var settings = store.Load();

        Assert.Equal(ElevationMode.Auto, settings.ElevationMode);
        Assert.Equal("Midnight", settings.Theme);
    }

    [Fact]
    public void Load_WithInvalidJsonFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        var settings = store.Load();

        Assert.Equal("", settings.DefaultScope);
        Assert.True(settings.AcceptAgreements);
        Assert.True(settings.IncludeUnknown);
        Assert.Equal("Midnight", settings.Theme);
    }

    [Fact]
    public void Save_WritesCamelCaseJsonWithNoBom()
    {
        var store = new SettingsStore(_directoryPath);

        store.Save(new WingmanSettings());

        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"defaultScope\"", text);
        Assert.Contains("\"acceptAgreements\"", text);
        Assert.Contains("\"includeUnknown\"", text);
        Assert.Contains("\"elevationMode\": \"auto\"", text);
        Assert.Contains("\"continueOnFailure\"", text);
        Assert.Contains("\"theme\"", text);
        Assert.Contains("\"autoUpdateWingman\": true", text);
    }
}
