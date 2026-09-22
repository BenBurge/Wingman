using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class WingetTableParserTests
{
    private const string ListOutput =
        "-                    \\\n" +
        "Name                                Id                             Version       Available     Source\n" +
        "------------------------------------------------------------------------------------------------------\n" +
        "7-Zip 23.01                         7zip.7zip                      23.01                       winget\n" +
        "Microsoft Edge                      Microsoft.Edge                 128.0.2739.42 129.0.2792.65 winget\n" +
        "Visual Studio Code (User)           Microsoft.VisualStudioCode     1.93.1                      winget\n" +
        "\n";

    private const string SearchOutput =
        "|\n" +
        "\\\n" +
        "Name                     Id                         Version\n" +
        "-----------------------------------------------------------\n" +
        "Git                      Git.Git                    2.46.2\n" +
        "Git Credential Manager   Git.Credential-Manager     2.6.1\n" +
        "2 package(s) found matching input criteria.\n";

    private const string UpgradeOutput =
        "/\n" +
        "Name             Id                    Version    Available   Source\n" +
        "-----------------------------------------------------------------------\n" +
        "Git              Git.Git               2.45.0     2.46.2      winget\n" +
        "\n" +
        "1 upgrades available.\n";

    [Fact]
    public void Parse_ListOutput_ReturnsAllRowsWithAvailableAndSource()
    {
        var rows = WingetTableParser.Parse(ListOutput);

        Assert.Equal(3, rows.Count);

        Assert.Equal("7-Zip 23.01", rows[0].Name);
        Assert.Equal("7zip.7zip", rows[0].Id);
        Assert.Equal("23.01", rows[0].Version);
        Assert.Null(rows[0].AvailableVersion);
        Assert.Equal("winget", rows[0].Source);

        Assert.Equal("Microsoft Edge", rows[1].Name);
        Assert.Equal("Microsoft.Edge", rows[1].Id);
        Assert.Equal("128.0.2739.42", rows[1].Version);
        Assert.Equal("129.0.2792.65", rows[1].AvailableVersion);
        Assert.Equal("winget", rows[1].Source);

        Assert.Equal("Visual Studio Code (User)", rows[2].Name);
        Assert.Equal("Microsoft.VisualStudioCode", rows[2].Id);
    }

    [Fact]
    public void Parse_SearchOutput_HasNullAvailableVersionAndStopsAtCountLine()
    {
        var rows = WingetTableParser.Parse(SearchOutput);

        Assert.Equal(2, rows.Count);

        Assert.Equal("Git", rows[0].Name);
        Assert.Equal("Git.Git", rows[0].Id);
        Assert.Equal("2.46.2", rows[0].Version);
        Assert.Null(rows[0].AvailableVersion);
        Assert.Equal("", rows[0].Source);

        Assert.Equal("Git Credential Manager", rows[1].Name);
        Assert.Equal("Git.Credential-Manager", rows[1].Id);
        Assert.Equal("2.6.1", rows[1].Version);
    }

    [Fact]
    public void Parse_UpgradeOutput_MapsAvailableColumnAndStopsAtBlankLine()
    {
        var rows = WingetTableParser.Parse(UpgradeOutput);

        Assert.Single(rows);
        Assert.Equal("Git", rows[0].Name);
        Assert.Equal("2.45.0", rows[0].Version);
        Assert.Equal("2.46.2", rows[0].AvailableVersion);
        Assert.Equal("winget", rows[0].Source);
    }

    [Fact]
    public void Parse_NoHeaderLine_ReturnsEmpty()
    {
        var rows = WingetTableParser.Parse("No package found matching input criteria.\n");

        Assert.Empty(rows);
    }
}
