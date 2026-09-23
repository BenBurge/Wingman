using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class UpgradeFixtureParserTests
{
    private const string MultiTableOutput =
        "Name                  Id                    Version   Available  Source\n" +
        "-----------------------------------------------------------------------\n" +
        "Git                   Git.Git               2.45.0    2.46.2     winget\n" +
        "1 upgrades available.\n" +
        "\n" +
        "The following packages have an upgrade available, but require explicit targeting for upgrade:\n" +
        "Name                  Id                    Version   Available  Source\n" +
        "-----------------------------------------------------------------------\n" +
        "Some Tool             Vendor.SomeTool       1.0.0     2.0.0      winget\n" +
        "Other Tool            Vendor.OtherTool      3.1       3.2        winget\n";

    private const string CannotBeDeterminedOutput =
        "Name             Id                    Version    Available   Source\n" +
        "-----------------------------------------------------------------------\n" +
        "Git              Git.Git               2.45.0     2.46.2      winget\n" +
        "2 package(s) have version numbers that cannot be determined. Use --include-unknown to see all results.\n";

    private const string PinnedOutput =
        "Name             Id                    Version    Available   Source\n" +
        "-----------------------------------------------------------------------\n" +
        "Git              Git.Git               2.45.0     2.46.2      winget\n" +
        "1 package(s) have pins that prevent upgrade. Use --include-pinned to see all results.\n";

    private const string NoMatchOutput = "No installed package found matching input criteria.\n";

    [Fact]
    public void Parse_UpgradeFixture_ReturnsAllSeventeenRowsUnflagged()
    {
        var rows = WingetTableParser.Parse(Fixtures.Load("upgrade.txt"));

        Assert.Equal(17, rows.Count);
        foreach (var row in rows)
        {
            Assert.False(row.RequiresExplicitTargeting);
        }
    }

    [Fact]
    public void Parse_UpgradeFixture_MatchesNotableRows()
    {
        var rows = WingetTableParser.Parse(Fixtures.Load("upgrade.txt"));

        var visualStudio = new PackageRow(
            "Visual Studio Professional 2022",
            "Microsoft.VisualStudio.2022.Professional",
            "< 17.14.41",
            "17.14.41",
            "winget");
        var azurePowerShell = new PackageRow(
            "Microsoft Azure PowerShell - August 2026",
            "Microsoft.Azure.Az",
            "16.2.0.41027",
            "16.3.0.41101",
            "winget");
        var wsl = new PackageRow(
            "Windows Subsystem for Linux",
            "Microsoft.WSL",
            "2.7.12.0",
            "2.7.13",
            "winget");

        Assert.Equal(visualStudio, rows[14]);
        Assert.Equal(azurePowerShell, rows[7]);
        Assert.Equal(wsl, rows[^1]);
    }

    [Fact]
    public void Parse_UpgradeIncludeUnknownFixture_MatchesUpgradeFixture()
    {
        var upgradeRows = WingetTableParser.Parse(Fixtures.Load("upgrade.txt"));
        var includeUnknownRows = WingetTableParser.Parse(Fixtures.Load("upgrade-include-unknown.txt"));

        Assert.Equal(17, includeUnknownRows.Count);
        Assert.Equal(upgradeRows, includeUnknownRows);
    }

    [Fact]
    public void Parse_MultiTableOutput_FlagsOnlyExplicitTargetingRows()
    {
        var rows = WingetTableParser.Parse(MultiTableOutput);

        Assert.Equal(3, rows.Count);

        var git = new PackageRow("Git", "Git.Git", "2.45.0", "2.46.2", "winget");
        var someTool = new PackageRow(
            "Some Tool", "Vendor.SomeTool", "1.0.0", "2.0.0", "winget", RequiresExplicitTargeting: true);
        var otherTool = new PackageRow(
            "Other Tool", "Vendor.OtherTool", "3.1", "3.2", "winget", RequiresExplicitTargeting: true);

        Assert.Equal(git, rows[0]);
        Assert.False(rows[0].RequiresExplicitTargeting);
        Assert.Equal(someTool, rows[1]);
        Assert.Equal(otherTool, rows[2]);
    }

    [Fact]
    public void Parse_CannotBeDeterminedTrailer_StopsTableAtTrailerLine()
    {
        var rows = WingetTableParser.Parse(CannotBeDeterminedOutput);

        Assert.Single(rows);
        Assert.Equal("Git", rows[0].Name);
    }

    [Fact]
    public void Parse_PinnedTrailer_StopsTableAtTrailerLine()
    {
        var rows = WingetTableParser.Parse(PinnedOutput);

        Assert.Single(rows);
        Assert.Equal("Git", rows[0].Name);
    }

    [Fact]
    public void Parse_NoMatchOutput_ReturnsEmpty()
    {
        var rows = WingetTableParser.Parse(NoMatchOutput);

        Assert.Empty(rows);
    }
}
