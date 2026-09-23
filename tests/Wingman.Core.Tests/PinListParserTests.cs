using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class PinListParserTests
{
    private const string PinTableOutput =
        "Name          Id             Version  Source  Pin type  Pinned version\n" +
        "----------------------------------------------------------------------\n" +
        "Git           Git.Git        2.46.2   winget  Blocking\n" +
        "Some App      Vendor.App     1.2.3    winget  Gating    1.2.*\n" +
        "Other App     Vendor.Other   4.0      winget  Pinning\n";

    [Fact]
    public void Parse_PinListFixtureWithNoPins_ReturnsEmpty()
    {
        var pins = WingetPinListParser.Parse(Fixtures.Load("pin-list.txt"));

        Assert.Empty(pins);
    }

    [Fact]
    public void Parse_PinTable_ReturnsEveryPinWithEveryColumn()
    {
        var pins = WingetPinListParser.Parse(PinTableOutput);

        Pin[] expected =
        [
            new("Git.Git", "Git", "2.46.2", "winget", PinType.Blocking, ""),
            new("Vendor.App", "Some App", "1.2.3", "winget", PinType.Gating, "1.2.*"),
            new("Vendor.Other", "Other App", "4.0", "winget", PinType.Pinning, ""),
        ];
        Assert.Equal(expected, pins);
    }

    [Fact]
    public void Parse_PinTableWithCrLf_MatchesLfOutput()
    {
        var pins = WingetPinListParser.Parse(PinTableOutput.Replace("\n", "\r\n"));

        Assert.Equal(WingetPinListParser.Parse(PinTableOutput), pins);
    }
}
