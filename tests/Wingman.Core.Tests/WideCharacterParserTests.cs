using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

/// <summary>
/// <c>search-git.txt</c> is the fixture with East Asian wide characters in package names. winget
/// pads each of them as two cells, so these rows are shorter in chars than the header.
/// </summary>
public class WideCharacterParserTests
{
    private static readonly IReadOnlyList<PackageRow> Rows = WingetTableParser.Parse(Fixtures.Load("search-git.txt"));

    private static PackageRow SingleRowWithId(string id)
    {
        var matches = new List<PackageRow>();
        foreach (var row in Rows)
        {
            if (row.Id == id)
            {
                matches.Add(row);
            }
        }

        return Assert.Single(matches);
    }

    [Fact]
    public void Parse_NameWithTwoWideCharacters_AlignsColumns()
    {
        var expected = new PackageRow("中文Git", "DuckStudio.ChineseGit", "3.4", null, "winget");

        Assert.Equal(expected, SingleRowWithId("DuckStudio.ChineseGit"));
    }

    [Fact]
    public void Parse_NameOfOnlyWideCharacters_AlignsColumns()
    {
        var expected = new PackageRow("百度语音输入", "Baidu.BaiduSpeechInput", "2.0.0.33", null, "winget");

        Assert.Equal(expected, SingleRowWithId("Baidu.BaiduSpeechInput"));
    }

    [Fact]
    public void Parse_NameMixingLatinAndWideCharactersWithBlankMatch_AlignsColumns()
    {
        var expected = new PackageRow("GitHub 标签管理器", "DuckStudio.GitHubLabelsManager", "1.13", null, "winget");

        Assert.Equal(expected, SingleRowWithId("DuckStudio.GitHubLabelsManager"));
    }
}
