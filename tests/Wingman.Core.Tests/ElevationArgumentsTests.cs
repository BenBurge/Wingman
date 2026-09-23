using Wingman.Core.Elevation;

namespace Wingman.Core.Tests;

public class ElevationArgumentsTests
{
    [Fact]
    public void TryParse_PipeAndParent_ReturnsBoth()
    {
        var parsed = ElevatedWorkerArguments.TryParse(
            ["--elevated-worker", "wingman-elevated-abc", "--parent", "4242"], out var pipe, out var parentPid);

        Assert.True(parsed);
        Assert.Equal("wingman-elevated-abc", pipe);
        Assert.Equal(4242, parentPid);
    }

    [Fact]
    public void TryParse_PipeOnly_ReturnsPipeWithoutParent()
    {
        var parsed = ElevatedWorkerArguments.TryParse(
            ["--elevated-worker", "wingman-elevated-abc"], out var pipe, out var parentPid);

        Assert.True(parsed);
        Assert.Equal("wingman-elevated-abc", pipe);
        Assert.Null(parentPid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("99999999999")]
    public void TryParse_ParentNotAPositiveInteger_Fails(string pid)
    {
        var parsed = ElevatedWorkerArguments.TryParse(
            ["--elevated-worker", "wingman-elevated-abc", "--parent", pid], out var pipe, out var parentPid);

        Assert.False(parsed);
        Assert.Equal(string.Empty, pipe);
        Assert.Null(parentPid);
    }

    [Theory]
    [MemberData(nameof(MalformedCommandLines))]
    public void TryParse_MalformedCommandLine_Fails(string[] args)
    {
        Assert.False(ElevatedWorkerArguments.TryParse(args, out _, out _));
    }

    public static TheoryData<string[]> MalformedCommandLines() => new()
    {
        Array.Empty<string>(),
        new[] { "--elevated-worker" },
        new[] { "--elevated-worker", "" },
        new[] { "--elevated-worker", "pipe", "--parent" },
        new[] { "--elevated-worker", "pipe", "--other", "5" },
        new[] { "--elevated-worker", "pipe", "--parent", "5", "extra" },
        new[] { "--elevated-worker", "pipe", "5" },
        new[] { "upgrade", "pipe", "--parent", "5" },
        new[] { "--parent", "5", "--elevated-worker", "pipe" },
    };
}
