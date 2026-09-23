using System.Text;
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
        Assert.True(settings.ContinueOnFailure);
        Assert.Equal("Midnight", settings.Theme);
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
            ContinueOnFailure = false,
            Theme = "Auto",
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("machine", loaded.DefaultScope);
        Assert.False(loaded.AcceptAgreements);
        Assert.False(loaded.IncludeUnknown);
        Assert.Equal(ElevationMode.Always, loaded.ElevationMode);
        Assert.False(loaded.ContinueOnFailure);
        Assert.Equal("Auto", loaded.Theme);
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
    }
}
