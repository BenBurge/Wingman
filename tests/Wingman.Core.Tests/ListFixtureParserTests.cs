using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class ListFixtureParserTests
{
    private static readonly IReadOnlyList<PackageRow> Rows = WingetTableParser.Parse(Fixtures.Load("list.txt"));

    private static List<PackageRow> RowsWithId(string id)
    {
        var matches = new List<PackageRow>();
        foreach (var row in Rows)
        {
            if (row.Id == id)
            {
                matches.Add(row);
            }
        }

        return matches;
    }

    private static PackageRow SingleRowWithId(string id) => Assert.Single(RowsWithId(id));

    [Fact]
    public void Parse_ListFixture_ReturnsEveryRow()
    {
        Assert.Equal(212, Rows.Count);
    }

    [Fact]
    public void Parse_ListFixture_NonWingetRowsHaveBlankAvailableAndSource()
    {
        var arpId = @"ARP\Machine\X64\{5EC2AC66-2764-44EC-A2AD-47F9714413BF}";
        var msixId = @"MSIX\AdobeAcrobatReaderCoreApp_26.0.0.1_x64__33qsgwvbcw8mw";

        Assert.Equal(new PackageRow("ActiveBatch V14", arpId, "14.0.5.425", null, ""), SingleRowWithId(arpId));
        Assert.Equal(new PackageRow("Adobe Acrobat Reader", msixId, "26.0.0.1", null, ""), SingleRowWithId(msixId));
    }

    [Fact]
    public void Parse_ListFixture_RowWithUpdateHasAvailableVersion()
    {
        var expected = new PackageRow("AutoHotkey", "AutoHotkey.AutoHotkey", "2.0.26", "2.0.28", "winget");

        Assert.Equal(expected, SingleRowWithId("AutoHotkey.AutoHotkey"));
    }

    [Fact]
    public void Parse_ListFixture_RegisteredSignInNameKeepsColumnsAligned()
    {
        var id = @"MSIX\AppUp.IntelConnectivityPerformanceSuite_50.26.401.0_x64__8j3eq9eme6ctt";
        var expected = new PackageRow("Intel® Connectivity Performance Suite", id, "50.26.401.0", null, "");

        Assert.Equal(expected, SingleRowWithId(id));
    }

    [Fact]
    public void Parse_ListFixture_KeepsComparisonPrefixedVersionsVerbatim()
    {
        var developerPackId = "Microsoft.DotNet.Framework.DeveloperPack.4.6";
        PackageRow[] expectedDeveloperPacks =
        [
            new("Microsoft .NET Framework 4.8.1 Targeting Pack", developerPackId, "> 4.6.2", null, "winget"),
            new("Microsoft .NET Framework 4.8.1 Targeting Pack (ENU)", developerPackId, "> 4.6.2", null, "winget"),
        ];
        var visualStudioId = "Microsoft.VisualStudio.2022.Professional";
        var expectedVisualStudio = new PackageRow(
            "Visual Studio Professional 2022", visualStudioId, "< 17.14.41", "17.14.41", "winget");

        Assert.Equal(expectedDeveloperPacks, RowsWithId(developerPackId));
        Assert.Equal(expectedVisualStudio, SingleRowWithId(visualStudioId));
    }

    [Fact]
    public void Parse_ListFixture_KeepsIdenticalDuplicateRows()
    {
        var id = "Microsoft.WindowsAppRuntime.1.8";
        var duplicated = new PackageRow("WindowsAppRuntime.1.8", id, "> 1.8.10", null, "winget");
        var distinct = new PackageRow("WindowsAppRuntime.1.8", id, "1.8.10", null, "winget");

        Assert.Equal([duplicated, duplicated, distinct], RowsWithId(id));
    }
}
