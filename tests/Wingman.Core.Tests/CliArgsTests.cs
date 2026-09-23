using Wingman.Cli;

namespace Wingman.Core.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_Empty_HasNoCommandPositionalsOrOptions()
    {
        var args = CliArgs.Parse([]);

        Assert.Null(args.Command);
        Assert.Empty(args.Positionals);
        Assert.Empty(args.Options);
    }

    [Fact]
    public void Parse_FirstNonOptionIsCommand_RestArePositionalsInOrder()
    {
        var args = CliArgs.Parse(["upgrade", "Git.Git", "Microsoft.PowerShell"]);

        Assert.Equal("upgrade", args.Command);
        Assert.Equal(["Git.Git", "Microsoft.PowerShell"], args.Positionals);
    }

    [Fact]
    public void Parse_FlagWithoutValue_MapsToEmptyString()
    {
        var args = CliArgs.Parse(["upgrade", "--all"]);

        Assert.True(args.HasFlag("all"));
        Assert.Equal("", args.GetOption("all"));
    }

    [Fact]
    public void Parse_KeyFollowedByValue_TakesTheValue()
    {
        var args = CliArgs.Parse(["history", "--last", "5", "--failed"]);

        Assert.Equal("history", args.Command);
        Assert.Empty(args.Positionals);
        Assert.Equal("5", args.GetOption("last"));
        Assert.Equal("", args.GetOption("failed"));
    }

    [Fact]
    public void Parse_KeyFollowedByOption_IsAFlag()
    {
        var args = CliArgs.Parse(["check", "--notify", "--json"]);

        Assert.Equal("", args.GetOption("notify"));
        Assert.True(args.HasFlag("json"));
    }

    [Fact]
    public void Parse_KeyEqualsValue_SplitsAtFirstEquals()
    {
        var args = CliArgs.Parse(["install", "--version=1.2=3", "Git.Git"]);

        Assert.Equal("1.2=3", args.GetOption("version"));
        Assert.Equal(["Git.Git"], args.Positionals);
    }

    [Fact]
    public void Parse_ShortH_MapsToHelp()
    {
        var args = CliArgs.Parse(["check", "-h"]);

        Assert.Equal("check", args.Command);
        Assert.True(args.HasFlag("help"));
    }

    [Fact]
    public void Parse_GlobalSwitchesNeverTakeAValue()
    {
        var args = CliArgs.Parse(["--fake", "check", "--json", "extra"]);

        Assert.Equal("check", args.Command);
        Assert.Equal(["extra"], args.Positionals);
        Assert.Equal("", args.GetOption("fake"));
        Assert.Equal("", args.GetOption("json"));
    }

    [Fact]
    public void Parse_OptionNames_AreCaseInsensitive()
    {
        var args = CliArgs.Parse(["list", "--JSON"]);

        Assert.True(args.HasFlag("json"));
        Assert.Equal("", args.GetOption("Json"));
    }

    [Fact]
    public void Parse_AfterDoubleDash_TokensAreNotOptions()
    {
        var args = CliArgs.Parse(["search", "--", "--weird", "-h"]);

        Assert.Equal("search", args.Command);
        Assert.Equal(["--weird", "-h"], args.Positionals);
        Assert.Empty(args.Options);
    }

    [Fact]
    public void GetOption_Missing_ReturnsNull()
    {
        var args = CliArgs.Parse(["check"]);

        Assert.False(args.HasFlag("json"));
        Assert.Null(args.GetOption("json"));
    }
}
