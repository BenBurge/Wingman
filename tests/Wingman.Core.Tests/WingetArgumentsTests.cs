using Wingman.Core.Models;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class WingetArgumentsTests
{
    private static readonly OperationRequest EveryOption = new(
        Id: "Git.Git",
        Version: "2.46.2",
        Scope: "machine",
        Architecture: "x64",
        Interactive: true,
        SkipHashCheck: true,
        Force: true,
        CustomArguments: ["--override", "/SILENT /NORESTART"]);

    [Fact]
    public void Install_IdOnly_EmitsRequiredFlags()
    {
        string[] expected =
        [
            "install", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        Assert.Equal(expected, WingetArguments.Install(new OperationRequest("Git.Git")));
    }

    [Fact]
    public void Install_EveryOption_EmitsOptionsInOrderAndCustomArgumentsLast()
    {
        string[] expected =
        [
            "install", "--id", "Git.Git", "--exact",
            "--version", "2.46.2",
            "--scope", "machine",
            "--architecture", "x64",
            "--interactive",
            "--ignore-security-hash",
            "--force",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
            "--override", "/SILENT /NORESTART",
        ];

        Assert.Equal(expected, WingetArguments.Install(EveryOption));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("scope")]
    [InlineData("architecture")]
    [InlineData("interactive")]
    [InlineData("skip-hash")]
    [InlineData("force")]
    [InlineData("custom")]
    public void Install_SingleOption_EmitsOnlyThatOption(string option)
    {
        var (request, optionArguments) = SingleOption(option);
        var customArguments = option == "custom" ? optionArguments : [];
        var flagArguments = option == "custom" ? [] : optionArguments;
        string[] expected =
        [
            "install", "--id", "Git.Git", "--exact",
            .. flagArguments,
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
            .. customArguments,
        ];

        Assert.Equal(expected, WingetArguments.Install(request));
    }

    [Fact]
    public void Upgrade_IdOnly_EmitsRequiredFlags()
    {
        string[] expected =
        [
            "upgrade", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        Assert.Equal(expected, WingetArguments.Upgrade(new OperationRequest("Git.Git")));
    }

    [Fact]
    public void Upgrade_EveryOption_EmitsOptionsInOrderAndCustomArgumentsLast()
    {
        string[] expected =
        [
            "upgrade", "--id", "Git.Git", "--exact",
            "--version", "2.46.2",
            "--scope", "machine",
            "--architecture", "x64",
            "--interactive",
            "--ignore-security-hash",
            "--force",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
            "--override", "/SILENT /NORESTART",
        ];

        Assert.Equal(expected, WingetArguments.Upgrade(EveryOption));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("scope")]
    [InlineData("architecture")]
    [InlineData("interactive")]
    [InlineData("skip-hash")]
    [InlineData("force")]
    [InlineData("custom")]
    public void Upgrade_SingleOption_EmitsOnlyThatOption(string option)
    {
        var (request, optionArguments) = SingleOption(option);
        var customArguments = option == "custom" ? optionArguments : [];
        var flagArguments = option == "custom" ? [] : optionArguments;
        string[] expected =
        [
            "upgrade", "--id", "Git.Git", "--exact",
            .. flagArguments,
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
            .. customArguments,
        ];

        Assert.Equal(expected, WingetArguments.Upgrade(request));
    }

    [Fact]
    public void Uninstall_IdOnly_EmitsRequiredFlagsWithoutPackageAgreements()
    {
        string[] expected =
        [
            "uninstall", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.Uninstall(new OperationRequest("Git.Git")));
    }

    [Fact]
    public void Uninstall_EveryOption_DropsUnsupportedFlagsAndPutsCustomArgumentsLast()
    {
        string[] expected =
        [
            "uninstall", "--id", "Git.Git", "--exact",
            "--version", "2.46.2",
            "--scope", "machine",
            "--interactive",
            "--force",
            "--disable-interactivity", "--accept-source-agreements",
            "--override", "/SILENT /NORESTART",
        ];

        var arguments = WingetArguments.Uninstall(EveryOption);

        Assert.Equal(expected, arguments);
        Assert.DoesNotContain("--architecture", arguments);
        Assert.DoesNotContain("--ignore-security-hash", arguments);
        Assert.DoesNotContain("--accept-package-agreements", arguments);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("scope")]
    [InlineData("interactive")]
    [InlineData("force")]
    [InlineData("custom")]
    public void Uninstall_SingleSupportedOption_EmitsOnlyThatOption(string option)
    {
        var (request, optionArguments) = SingleOption(option);
        var customArguments = option == "custom" ? optionArguments : [];
        var flagArguments = option == "custom" ? [] : optionArguments;
        string[] expected =
        [
            "uninstall", "--id", "Git.Git", "--exact",
            .. flagArguments,
            "--disable-interactivity", "--accept-source-agreements",
            .. customArguments,
        ];

        Assert.Equal(expected, WingetArguments.Uninstall(request));
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData("skip-hash")]
    public void Uninstall_SingleUnsupportedOption_IsDropped(string option)
    {
        var (request, _) = SingleOption(option);
        string[] expected =
        [
            "uninstall", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.Uninstall(request));
    }

    [Fact]
    public void PinAdd_IdOnly_EmitsRequiredFlags()
    {
        string[] expected =
        [
            "pin", "add", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.PinAdd("Git.Git", blocking: false, version: null));
    }

    [Fact]
    public void PinAdd_Blocking_EmitsBlockingFlag()
    {
        string[] expected =
        [
            "pin", "add", "--id", "Git.Git", "--exact", "--blocking",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.PinAdd("Git.Git", blocking: true, version: null));
    }

    [Fact]
    public void PinAdd_Version_EmitsVersionOption()
    {
        string[] expected =
        [
            "pin", "add", "--id", "Git.Git", "--exact", "--version", "2.46.*",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.PinAdd("Git.Git", blocking: false, version: "2.46.*"));
    }

    [Fact]
    public void PinAdd_BlockingAndVersion_EmitsBlockingBeforeVersion()
    {
        string[] expected =
        [
            "pin", "add", "--id", "Git.Git", "--exact", "--blocking", "--version", "2.46.2",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.PinAdd("Git.Git", blocking: true, version: "2.46.2"));
    }

    [Fact]
    public void PinRemove_EmitsRequiredFlags()
    {
        string[] expected =
        [
            "pin", "remove", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        Assert.Equal(expected, WingetArguments.PinRemove("Git.Git"));
    }

    private static (OperationRequest Request, string[] Arguments) SingleOption(string option) => option switch
    {
        "version" => (new OperationRequest("Git.Git", Version: "2.46.2"), ["--version", "2.46.2"]),
        "scope" => (new OperationRequest("Git.Git", Scope: "user"), ["--scope", "user"]),
        "architecture" => (new OperationRequest("Git.Git", Architecture: "arm64"), ["--architecture", "arm64"]),
        "interactive" => (new OperationRequest("Git.Git", Interactive: true), ["--interactive"]),
        "skip-hash" => (new OperationRequest("Git.Git", SkipHashCheck: true), ["--ignore-security-hash"]),
        "force" => (new OperationRequest("Git.Git", Force: true), ["--force"]),
        "custom" => (new OperationRequest("Git.Git", CustomArguments: ["--log", "C:\\temp\\git.log"]), ["--log", "C:\\temp\\git.log"]),
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
    };
}
