using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class FixtureSmokeTests
{
    private static readonly string[] AllReadmeFixtures =
    [
        "list.txt",
        "upgrade.txt",
        "upgrade-include-unknown.txt",
        "search-vscode.txt",
        "search-git.txt",
        "search-nomatch.txt",
        "show-vscode.txt",
        "show-versions-git.txt",
        "pin-list.txt",
        "version.txt",
        "exit-codes.txt",
    ];

    /// <summary>
    /// Reads a captured winget fixture from the test output's Fixtures folder, where the
    /// csproj copies <c>Fixtures/**</c> with <c>PreserveNewest</c>.
    /// </summary>
    private static string LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("list.txt", true)]
    [InlineData("upgrade.txt", true)]
    [InlineData("upgrade-include-unknown.txt", true)]
    [InlineData("search-git.txt", true)]
    [InlineData("search-vscode.txt", true)]
    [InlineData("search-nomatch.txt", false)]
    public void Parse_TableFixture_MatchesExpectedRowPresence(string fileName, bool expectRows)
    {
        var output = LoadFixture(fileName);

        var rows = WingetTableParser.Parse(output);

        if (expectRows)
        {
            Assert.True(rows.Count > 0, $"Expected at least one row from {fileName}, got 0.");
        }
        else
        {
            Assert.Empty(rows);
        }
    }

    [Fact]
    public void AllReadmeFixtures_ExistAndHaveNoUtf8Bom()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        foreach (var fileName in AllReadmeFixtures)
        {
            var path = Path.Combine(fixturesDir, fileName);
            Assert.True(File.Exists(path), $"Expected fixture file missing: {fileName}");

            var firstBytes = new byte[3];
            using (var stream = File.OpenRead(path))
            {
                stream.ReadExactly(firstBytes);
            }

            var isUtf8Bom = firstBytes is [0xEF, 0xBB, 0xBF];
            Assert.False(isUtf8Bom, $"{fileName} starts with a UTF-8 BOM; recapture with Write-Utf8NoBom.");
        }
    }
}
