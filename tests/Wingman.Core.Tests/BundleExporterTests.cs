using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Options;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class BundleExporterTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    private static readonly IReadOnlyList<PackageRow> ListRows = WingetTableParser.Parse(Fixtures.Load("list.txt"));

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    [Fact]
    public void Build_AllRowsSelected_SplitsWingetAndArpMsixRowsWithInfoSentence()
    {
        var options = new PackageOptionsStore(_directoryPath);

        var bundle = BundleExporter.Build(ListRows, options, _ => true, includeOptions: true, includeUpdatesOptions: true);

        var expectedCompatible = ListRows.Count(BundleExporter.IsCompatible);
        var expectedIncompatible = ListRows.Count - expectedCompatible;

        Assert.Equal(expectedCompatible, bundle.Packages.Count);
        Assert.Equal(expectedIncompatible, bundle.IncompatiblePackages.Count);
        Assert.All(bundle.Packages, p => Assert.Equal("winget", p.Source));
        Assert.All(bundle.Packages, p => Assert.Equal("WinGet", p.ManagerName));
        Assert.All(bundle.IncompatiblePackages, p => Assert.NotEqual("winget", p.Source, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            "The following packages could not be exported because they are not from a supported package manager.",
            bundle.IncompatiblePackagesInfo);
    }

    [Fact]
    public void Build_NoIncompatibleRowsSelected_LeavesInfoSentenceBlank()
    {
        var options = new PackageOptionsStore(_directoryPath);

        var bundle = BundleExporter.Build(ListRows, options, BundleExporter.IsCompatible, includeOptions: true, includeUpdatesOptions: true);

        Assert.Empty(bundle.IncompatiblePackages);
        Assert.Equal("", bundle.IncompatiblePackagesInfo);
    }

    [Fact]
    public void Build_RowWithCustomOptions_GetsInstallationOptionsWhileOthersStayNull()
    {
        var options = new PackageOptionsStore(_directoryPath);
        options.SetInstallOptions("AutoHotkey.AutoHotkey", new InstallOptions
        {
            RunAsAdministrator = true,
            Architecture = "x64",
        });

        var bundle = BundleExporter.Build(ListRows, options, BundleExporter.IsCompatible, includeOptions: true, includeUpdatesOptions: true);

        var withOptions = bundle.Packages.Single(p => p.Id == "AutoHotkey.AutoHotkey");
        Assert.NotNull(withOptions.InstallationOptions);
        Assert.True(withOptions.InstallationOptions!.RunAsAdministrator);
        Assert.Equal("x64", withOptions.InstallationOptions.Architecture);

        var withoutOptions = bundle.Packages.Where(p => p.Id != "AutoHotkey.AutoHotkey");
        Assert.All(withoutOptions, p => Assert.Null(p.InstallationOptions));
    }

    [Fact]
    public void Build_IncludeOptionsFalse_DropsCustomInstallationOptions()
    {
        var options = new PackageOptionsStore(_directoryPath);
        options.SetInstallOptions("AutoHotkey.AutoHotkey", new InstallOptions { RunAsAdministrator = true });

        var bundle = BundleExporter.Build(ListRows, options, BundleExporter.IsCompatible, includeOptions: false, includeUpdatesOptions: true);

        var package = bundle.Packages.Single(p => p.Id == "AutoHotkey.AutoHotkey");
        Assert.Null(package.InstallationOptions);
    }

    [Fact]
    public void Build_UnselectedRows_AreAbsentFromBothLists()
    {
        var options = new PackageOptionsStore(_directoryPath);

        var bundle = BundleExporter.Build(
            ListRows, options, row => row.Id != "Git.Git" && row.Id != "AutoHotkey.AutoHotkey", includeOptions: true, includeUpdatesOptions: true);

        Assert.DoesNotContain(bundle.Packages, p => p.Id == "Git.Git");
        Assert.DoesNotContain(bundle.Packages, p => p.Id == "AutoHotkey.AutoHotkey");
        Assert.DoesNotContain(bundle.IncompatiblePackages, p => p.Id == "Git.Git");
    }

    [Fact]
    public void Build_ThenWriteAndRead_RoundTripsWithEqualCounts()
    {
        var options = new PackageOptionsStore(_directoryPath);
        options.SetInstallOptions("AutoHotkey.AutoHotkey", new InstallOptions { RunAsAdministrator = true });

        var bundle = BundleExporter.Build(ListRows, options, _ => true, includeOptions: true, includeUpdatesOptions: true);

        var json = BundleSerializer.Write(bundle);
        var roundTripped = BundleSerializer.Read(json);

        Assert.Equal(bundle.Packages.Count, roundTripped.Packages.Count);
        Assert.Equal(bundle.IncompatiblePackages.Count, roundTripped.IncompatiblePackages.Count);
        Assert.Equal(bundle.ExportVersion, roundTripped.ExportVersion);
    }
}
