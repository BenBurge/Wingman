using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class SearchFixtureParserTests
{
    private static readonly IReadOnlyList<PackageRow> VscodeRows = WingetTableParser.Parse(Fixtures.Load("search-vscode.txt"));

    private static readonly IReadOnlyList<PackageRow> GitRows = WingetTableParser.Parse(Fixtures.Load("search-git.txt"));

    private static PackageRow SingleRowWithId(IReadOnlyList<PackageRow> rows, string id)
    {
        var matches = new List<PackageRow>();
        foreach (var row in rows)
        {
            if (row.Id == id)
            {
                matches.Add(row);
            }
        }

        return Assert.Single(matches);
    }

    [Fact]
    public void Parse_SearchVscodeFixture_ReturnsEveryRow()
    {
        Assert.Equal(6, VscodeRows.Count);
    }

    [Theory]
    [InlineData("XP9KHM4BK9FZ7Q", "Visual Studio Code")]
    [InlineData("XP8LFCZM790F6B", "Visual Studio Code - Insiders")]
    public void Parse_SearchVscodeFixture_MsstoreRowsHaveUnknownVersion(string id, string name)
    {
        var expected = new PackageRow(name, id, "Unknown", null, "msstore");

        Assert.Equal(expected, SingleRowWithId(VscodeRows, id));
    }

    [Fact]
    public void Parse_SearchVscodeFixture_WingetRowMatchesWholeRecord()
    {
        var expected = new PackageRow(
            "Microsoft Visual Studio Code", "Microsoft.VisualStudioCode", "1.138.0", null, "winget");

        Assert.Equal(expected, SingleRowWithId(VscodeRows, "Microsoft.VisualStudioCode"));
    }

    [Fact]
    public void Parse_SearchGitFixture_ReturnsEveryRow()
    {
        Assert.Equal(362, GitRows.Count);
    }

    [Fact]
    public void Parse_SearchGitFixture_GitRowMatchesWholeRecord()
    {
        var expected = new PackageRow("Git", "Git.Git", "2.55.0.3", null, "winget");

        Assert.Equal(expected, SingleRowWithId(GitRows, "Git.Git"));
    }

    [Fact]
    public void Parse_SearchGitFixture_NonAsciiNameAndIdMatchWholeRecord()
    {
        var id = "ReceitaFederaldoBrasil.EscrituraçãoDigitalECF";
        var expected = new PackageRow("Escrituração Digital ECF", id, "12.2.7", null, "winget");

        Assert.Equal(expected, SingleRowWithId(GitRows, id));
    }

    [Fact]
    public void Parse_SearchGitFixture_TagMatchIsNotPartOfVersion()
    {
        var expected = new PackageRow("OpenCherry Desktop", "4nrry.OpenCherryDesktop", "0.0.1", null, "winget");

        Assert.Equal(expected, SingleRowWithId(GitRows, "4nrry.OpenCherryDesktop"));
    }

    [Fact]
    public void Parse_SearchGitFixture_LastRowParses()
    {
        var expected = new PackageRow("xploview", "xploview.xploview", "3.3.31", null, "winget");

        Assert.Equal(expected, GitRows[^1]);
    }
}
