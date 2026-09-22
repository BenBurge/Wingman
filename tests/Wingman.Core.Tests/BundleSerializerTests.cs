using Wingman.Core.Bundles;

namespace Wingman.Core.Tests;

public class BundleSerializerTests
{
    [Fact]
    public void WriteThenRead_RoundTripsTwoPackagesOneWithOptionsOneWithout()
    {
        var bundle = new Bundle
        {
            ExportVersion = 3,
            Packages =
            [
                new BundlePackage
                {
                    Id = "Microsoft.VisualStudioCode",
                    Name = "Visual Studio Code",
                    Version = "1.93.1",
                    Source = "winget",
                    ManagerName = "WinGet",
                    InstallationOptions = new InstallOptions
                    {
                        InteractiveInstallation = true,
                        RunAsAdministrator = true,
                        Architecture = "x64",
                        InstallationScope = "user",
                        CustomParameters_Install = ["/silent"],
                    },
                    Updates = new UpdatesOptions
                    {
                        UpdatesIgnored = true,
                        IgnoredVersion = "1.94.0",
                    },
                },
                new BundlePackage
                {
                    Id = "7zip.7zip",
                    Name = "7-Zip",
                    Version = "23.01",
                    Source = "winget",
                    ManagerName = "WinGet",
                },
            ],
        };

        var json = BundleSerializer.Write(bundle);

        Assert.Contains("\"export_version\": 3", json);
        Assert.Contains("\"ManagerName\": \"WinGet\"", json);

        var roundTripped = BundleSerializer.Read(json);

        Assert.Equal(2, roundTripped.Packages.Count);

        var withOptions = roundTripped.Packages.Single(p => p.Id == "Microsoft.VisualStudioCode");
        Assert.NotNull(withOptions.InstallationOptions);
        Assert.True(withOptions.InstallationOptions!.RunAsAdministrator);
        Assert.Equal("x64", withOptions.InstallationOptions.Architecture);
        Assert.NotNull(withOptions.Updates);
        Assert.Equal("1.94.0", withOptions.Updates!.IgnoredVersion);

        var withoutOptions = roundTripped.Packages.Single(p => p.Id == "7zip.7zip");
        Assert.Null(withoutOptions.InstallationOptions);
        Assert.Null(withoutOptions.Updates);
    }

    [Fact]
    public void Write_PackageWithoutOptions_OmitsInstallationOptionsKey()
    {
        var bundle = new Bundle
        {
            Packages = [new BundlePackage { Id = "7zip.7zip", Name = "7-Zip", Version = "23.01", Source = "winget", ManagerName = "WinGet" }],
        };

        var json = BundleSerializer.Write(bundle);

        Assert.DoesNotContain("InstallationOptions", json);
        Assert.DoesNotContain("\"Updates\"", json);
    }

    [Fact]
    public void Read_UniGetUiMinimalFixture_ParsesPackagesAndIncompatiblePackages()
    {
        const string json =
            """
            { "export_version": 2.1, "packages": [ { "Id": "Hello" }, { "Id": "World" } ], "incompatible_packages_info": "hey", "incompatible_packages": [ { "Id": "3" } ] }
            """;

        var bundle = BundleSerializer.Read(json);

        Assert.Equal(2.1, bundle.ExportVersion);
        Assert.Equal(2, bundle.Packages.Count);
        Assert.Equal("Hello", bundle.Packages[0].Id);
        Assert.Equal("World", bundle.Packages[1].Id);
        Assert.Equal("hey", bundle.IncompatiblePackagesInfo);
        Assert.Single(bundle.IncompatiblePackages);
        Assert.Equal("3", bundle.IncompatiblePackages[0].Id);
    }
}
