using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class VersionComparerTests
{
    [Theory]
    [InlineData("2.9.1", "2.10.0")]
    [InlineData("2.55.0", "2.55.0.3")]
    [InlineData("2.97.0", "2.98.0")]
    [InlineData("1.0-beta", "1.1")]
    public void Compare_OlderFirst_IsNegative(string older, string newer)
    {
        Assert.True(VersionComparer.Instance.Compare(older, newer) < 0);
        Assert.True(VersionComparer.Instance.Compare(newer, older) > 0);
    }

    [Theory]
    [InlineData("2.98.0", "2.98.0")]
    [InlineData("Unknown", "unknown")]
    public void Compare_SameVersion_IsZero(string left, string right)
    {
        Assert.Equal(0, VersionComparer.Instance.Compare(left, right));
    }
}
