using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class ElevationHeuristicTests
{
    private static PackageDetails Details(string installerType, string? scope = null)
    {
        var fields = new Dictionary<string, string>();
        if (scope is not null)
        {
            fields["Installer.Scope"] = scope;
        }

        return new PackageDetails { InstallerType = installerType, AdditionalFields = fields };
    }

    private static OperationPlan Plan(bool requiresElevation, bool forceElevation = false) => new(
        OperationKind.Upgrade, new OperationRequest("GitHub.cli"), requiresElevation, "", "", false, forceElevation);

    [Theory]
    [InlineData("msi")]
    [InlineData("wix")]
    [InlineData("burn")]
    [InlineData("exe")]
    [InlineData("inno")]
    [InlineData("nullsoft")]
    [InlineData("MSI")]
    [InlineData("Inno")]
    public void NeedsElevation_MachineWideType_ReturnsTrue(string installerType)
    {
        Assert.True(ElevationHeuristic.NeedsElevation(Details(installerType)));
    }

    [Theory]
    [InlineData("msi", "machine")]
    [InlineData("exe", "")]
    public void NeedsElevation_MachineWideTypeNotScopedToUser_ReturnsTrue(string installerType, string scope)
    {
        Assert.True(ElevationHeuristic.NeedsElevation(Details(installerType, scope)));
    }

    [Theory]
    [InlineData("msi", "user")]
    [InlineData("inno", "User")]
    [InlineData("nullsoft", "USER")]
    public void NeedsElevation_MachineWideTypeScopedToUser_ReturnsFalse(string installerType, string scope)
    {
        Assert.False(ElevationHeuristic.NeedsElevation(Details(installerType, scope)));
    }

    [Theory]
    [InlineData("msix")]
    [InlineData("appx")]
    [InlineData("portable")]
    [InlineData("zip")]
    [InlineData("MSIX")]
    public void NeedsElevation_UnelevatedType_ReturnsFalse(string installerType)
    {
        Assert.False(ElevationHeuristic.NeedsElevation(Details(installerType)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pwa")]
    public void NeedsElevation_EmptyOrUnknownType_ReturnsNull(string installerType)
    {
        Assert.Null(ElevationHeuristic.NeedsElevation(Details(installerType)));
    }

    [Fact]
    public void NeedsElevation_ShowVscodeFixture_ReadsTheInnoInstallerAsNeedingElevation()
    {
        var details = WingetShowParser.Parse(Fixtures.Load("show-vscode.txt"));

        Assert.NotNull(details);
        Assert.True(ElevationHeuristic.NeedsElevation(details));
    }

    [Theory]
    [InlineData(ElevationMode.Auto)]
    [InlineData(ElevationMode.Always)]
    [InlineData(ElevationMode.Never)]
    public void Resolve_ProcessElevated_ReturnsFalseInEveryMode(ElevationMode mode)
    {
        Assert.False(ElevationHeuristic.Resolve(Plan(requiresElevation: true), Details("msi"), mode, processIsElevated: true));
    }

    [Fact]
    public void Resolve_Never_ReturnsFalseEvenWhenThePlanNeedsElevation()
    {
        Assert.False(ElevationHeuristic.Resolve(Plan(requiresElevation: true), Details("msi"), ElevationMode.Never, false));
    }

    [Fact]
    public void Resolve_Always_ReturnsTrueEvenForAnUnelevatedInstaller()
    {
        Assert.True(ElevationHeuristic.Resolve(Plan(requiresElevation: false), Details("msix"), ElevationMode.Always, false));
    }

    [Fact]
    public void Resolve_AutoWithElevatedPlan_ReturnsTrueWithoutDetails()
    {
        Assert.True(ElevationHeuristic.Resolve(Plan(requiresElevation: true), null, ElevationMode.Auto, false));
    }

    [Fact]
    public void Resolve_AutoWithMsiInstaller_ReturnsTrueForAnUnelevatedPlan()
    {
        Assert.True(ElevationHeuristic.Resolve(Plan(requiresElevation: false), Details("msi"), ElevationMode.Auto, false));
    }

    [Theory]
    [InlineData("msix", null)]
    [InlineData("msi", "user")]
    [InlineData("", null)]
    public void Resolve_AutoWithInstallerNotNeedingElevation_ReturnsFalse(string installerType, string? scope)
    {
        var details = Details(installerType, scope);

        Assert.False(ElevationHeuristic.Resolve(Plan(requiresElevation: false), details, ElevationMode.Auto, false));
    }

    [Fact]
    public void Resolve_AutoWithNoDetails_ReturnsFalseForAnUnelevatedPlan()
    {
        Assert.False(ElevationHeuristic.Resolve(Plan(requiresElevation: false), null, ElevationMode.Auto, false));
    }

    [Theory]
    [InlineData(ElevationMode.Auto)]
    [InlineData(ElevationMode.Always)]
    [InlineData(ElevationMode.Never)]
    public void UsesHelper_ForcedPlan_ReturnsTrueInEveryMode(ElevationMode mode)
    {
        Assert.True(ElevationPolicy.UsesHelper(Plan(requiresElevation: false, forceElevation: true), mode, false));
    }

    [Fact]
    public void UsesHelper_ForcedPlanInElevatedProcess_ReturnsFalse()
    {
        Assert.False(ElevationPolicy.UsesHelper(Plan(requiresElevation: false, forceElevation: true), ElevationMode.Auto, true));
    }

    [Theory]
    [InlineData(ElevationMode.Auto, true, false, true)]
    [InlineData(ElevationMode.Auto, false, false, false)]
    [InlineData(ElevationMode.Always, false, false, true)]
    [InlineData(ElevationMode.Never, true, false, false)]
    [InlineData(ElevationMode.Always, true, true, false)]
    public void NeedsHelper_QueueAndList_AgreeWithUsesHelper(
        ElevationMode mode, bool requiresElevation, bool processIsElevated, bool expected)
    {
        var row = new PackageRow("GitHub CLI", "GitHub.cli", "2.60.0", "2.61.0", "winget");
        var operation = new QueuedOperation(OperationKind.Upgrade, row, Plan(requiresElevation));
        var queue = new OperationQueue();
        queue.Add(operation);

        Assert.Equal(expected, ElevationPolicy.NeedsHelper(queue, mode, processIsElevated));
        Assert.Equal(expected, ElevationPolicy.NeedsHelper([operation], mode, processIsElevated));
    }
}
