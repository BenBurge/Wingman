using System.Text;
using Wingman.Core.Bundles;
using Wingman.Core.Options;

namespace Wingman.Core.Tests;

public class PackageOptionsStoreTests : IDisposable
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
    public void Construct_WithNoFile_YieldsEmptyAndDoesNotCreateFile()
    {
        var store = new PackageOptionsStore(_directoryPath);

        Assert.Empty(store.AllIds());
        Assert.False(store.HasCustomInstallOptions("Git.Git"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void GetInstallOptions_WithNoEntry_ReturnsFreshDefault()
    {
        var store = new PackageOptionsStore(_directoryPath);

        var options = store.GetInstallOptions("Git.Git");

        Assert.True(options.IsDefault());
    }

    [Fact]
    public void GetUpdatesOptions_WithNoEntry_ReturnsFreshDefault()
    {
        var store = new PackageOptionsStore(_directoryPath);

        var options = store.GetUpdatesOptions("Discord.Discord");

        Assert.True(options.IsDefault());
    }

    [Fact]
    public void SetInstallOptionsThenReload_RoundTripsCustomizedOptions()
    {
        var store = new PackageOptionsStore(_directoryPath);
        var options = new InstallOptions
        {
            SkipHashCheck = true,
            InteractiveInstallation = true,
            RunAsAdministrator = true,
            Architecture = "x64",
            InstallationScope = "machine",
            CustomInstallLocation = @"C:\Tools\Git",
            Version = "2.44.0",
            CustomParameters_Install = ["--silent", "--no-desktop-icon"],
        };

        store.SetInstallOptions("Git.Git", options);
        var reloaded = new PackageOptionsStore(_directoryPath);
        var loaded = reloaded.GetInstallOptions("Git.Git");

        Assert.Equal(options, loaded);
    }

    [Fact]
    public void SetUpdatesOptionsThenReload_RoundTripsCustomizedOptions()
    {
        var store = new PackageOptionsStore(_directoryPath);
        var options = new UpdatesOptions
        {
            UpdatesIgnored = true,
            IgnoredVersion = "1.2.3",
        };

        store.SetUpdatesOptions("Discord.Discord", options);
        var reloaded = new PackageOptionsStore(_directoryPath);
        var loaded = reloaded.GetUpdatesOptions("Discord.Discord");

        Assert.Equal(options, loaded);
    }

    [Fact]
    public void SetInstallOptions_WithDefaultOptions_RemovesEntry()
    {
        var store = new PackageOptionsStore(_directoryPath);
        store.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });

        store.SetInstallOptions("Git.Git", new InstallOptions());

        Assert.False(store.HasCustomInstallOptions("Git.Git"));
        Assert.True(store.GetInstallOptions("Git.Git").IsDefault());
    }

    [Fact]
    public void SetUpdatesOptions_WithDefaultOptions_RemovesEntry()
    {
        var store = new PackageOptionsStore(_directoryPath);
        store.SetUpdatesOptions("Discord.Discord", new UpdatesOptions { UpdatesIgnored = true });

        store.SetUpdatesOptions("Discord.Discord", new UpdatesOptions());

        Assert.DoesNotContain("Discord.Discord", store.AllIds());
    }

    [Fact]
    public void SetToDefault_WithBothDictionariesEmpty_FileStillExistsWithEmptyObjects()
    {
        var store = new PackageOptionsStore(_directoryPath);
        store.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });

        store.SetInstallOptions("Git.Git", new InstallOptions());

        Assert.True(File.Exists(store.FilePath));
        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\"installOptions\": {}", text);
        Assert.Contains("\"updatesOptions\": {}", text);
    }

    [Fact]
    public void GetInstallOptions_KeysAreCaseInsensitive()
    {
        var store = new PackageOptionsStore(_directoryPath);
        store.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });

        var loaded = store.GetInstallOptions("git.git");

        Assert.True(loaded.SkipHashCheck);
        Assert.True(store.HasCustomInstallOptions("git.git"));
    }

    [Fact]
    public void AllIds_IsUnionOfBothDictionariesAndCaseInsensitive()
    {
        var store = new PackageOptionsStore(_directoryPath);
        store.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });
        store.SetUpdatesOptions("Discord.Discord", new UpdatesOptions { UpdatesIgnored = true });
        store.SetUpdatesOptions("git.git", new UpdatesOptions { IgnoredVersion = "1.0" });

        var ids = store.AllIds();

        Assert.Equal(2, ids.Count);
        Assert.Contains("Git.Git", ids);
        Assert.Contains("Discord.Discord", ids);
    }

    [Fact]
    public void Construct_WithInvalidJsonFile_LoadsEmpty()
    {
        var store = new PackageOptionsStore(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        File.WriteAllText(store.FilePath, "{ not valid json ");

        var reloaded = new PackageOptionsStore(_directoryPath);

        Assert.Empty(reloaded.AllIds());
    }

    [Fact]
    public void Construct_WithUnknownTopLevelKey_IsIgnored()
    {
        var directoryPath = _directoryPath;
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "package-options.json");
        File.WriteAllText(filePath, """
            {
              "installOptions": { "Git.Git": { "SkipHashCheck": true } },
              "somethingFromAFutureVersion": true
            }
            """);

        var store = new PackageOptionsStore(directoryPath);

        Assert.True(store.GetInstallOptions("Git.Git").SkipHashCheck);
    }

    [Fact]
    public void Save_WritesCamelCaseTopLevelKeysPascalCaseOptionsNoBomAndNoLeftoverTempFile()
    {
        var store = new PackageOptionsStore(_directoryPath);

        store.SetInstallOptions("Git.Git", new InstallOptions { SkipHashCheck = true });

        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"installOptions\"", text);
        Assert.Contains("\"updatesOptions\"", text);
        Assert.Contains("\"SkipHashCheck\"", text);
        Assert.DoesNotContain("\"skipHashCheck\"", text);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }
}
