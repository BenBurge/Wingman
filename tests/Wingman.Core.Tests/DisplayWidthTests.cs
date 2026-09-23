using System.Text;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class DisplayWidthTests
{
    [Theory]
    [InlineData(0x0041, 1)]  // A
    [InlineData(0x4E2D, 2)]  // 中
    [InlineData(0x00AE, 1)]  // ®
    [InlineData(0xFF41, 2)]  // fullwidth a
    [InlineData(0x0301, 0)]  // combining acute accent
    [InlineData(0x1F600, 2)] // grinning face emoji
    public void Of_Rune_ReturnsTerminalCellCount(int codePoint, int expectedWidth)
    {
        Assert.Equal(expectedWidth, DisplayWidth.Of(new Rune(codePoint)));
    }

    [Theory]
    [InlineData("Git", 3)]
    [InlineData("中文Git", 7)]
    [InlineData("é", 1)]
    [InlineData("\U0001F600!", 3)]
    public void Of_String_SumsRuneWidths(string text, int expectedWidth)
    {
        Assert.Equal(expectedWidth, DisplayWidth.Of(text));
    }
}
