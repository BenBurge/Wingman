using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class ShowParserTests
{
    private const string MultiLineOutput =
        "Found Sample App [Vendor.Sample]\r\n" +
        "Version: 1.0.0\r\n" +
        "Description: The first line of the description.\r\n" +
        "  The second line.\r\n" +
        "\r\n" +
        "  A paragraph after a blank line.\r\n" +
        "Release Notes: Fixed a crash on startup.\r\n" +
        "  - Faster search\r\n" +
        "  - Smaller download\r\n" +
        "Homepage: https://example.com/\r\n";

    private const string NoMatchOutput = "No package found matching input criteria.\r\n";

    [Fact]
    public void Parse_ShowVscodeFixture_MapsEveryField()
    {
        var details = WingetShowParser.Parse(Fixtures.Load("show-vscode.txt"));

        Assert.NotNull(details);
        Assert.Equal("Microsoft.VisualStudioCode", details.Id);
        Assert.Equal("Microsoft Visual Studio Code", details.Name);
        Assert.Equal("1.138.0", details.Version);
        Assert.Equal("Microsoft Corporation", details.Publisher);
        Assert.Equal("https://www.microsoft.com/", details.PublisherUrl);
        Assert.Equal("", details.Author);
        Assert.Equal("vscode", details.Moniker);
        Assert.Equal(
            "Microsoft Visual Studio Code is a code editor redefined and optimized for building and debugging " +
            "modern web and cloud applications. Microsoft Visual Studio Code is free and available on your " +
            "favorite platform - Linux, macOS, and Windows.",
            details.Description);
        Assert.Equal("https://code.visualstudio.com/", details.Homepage);
        Assert.Equal("Microsoft Software License", details.License);
        Assert.Equal("https://code.visualstudio.com/license", details.LicenseUrl);
        Assert.Equal("https://privacy.microsoft.com/", details.PrivacyUrl);
        Assert.Equal("", details.ReleaseNotes);
        Assert.Equal("https://code.visualstudio.com/updates/v1_138", details.ReleaseNotesUrl);
        Assert.Equal(["developer-tools", "editor"], details.Tags);
        Assert.Equal("inno", details.InstallerType);
        Assert.Equal(
            "https://vscode.download.prss.microsoft.com/dbazure/download/stable/" +
            "7debcd0e2acdea1c52de81bf9ee1620444407dda/VSCodeUserSetup-x64-1.138.0.exe",
            details.InstallerUrl);
        Assert.Equal("820df7a601d0179fc850433e1a1c047d2926a7a5a78ef01cd49fe8833fa2010d", details.InstallerSha256);
        Assert.Equal("en-US", details.InstallerLocale);
        Assert.Equal("", details.InstallerSize);

        var expectedAdditionalFields = new Dictionary<string, string>
        {
            ["Installer.Release Date"] = "2026-09-15",
            ["Installer.Offline Distribution Supported"] = "true",
        };
        Assert.Equal(expectedAdditionalFields, details.AdditionalFields);
    }

    [Fact]
    public void Parse_MultiLineDescriptionAndReleaseNotes_KeepsLineBreaks()
    {
        var details = WingetShowParser.Parse(MultiLineOutput);

        Assert.NotNull(details);
        Assert.Equal(
            "The first line of the description.\nThe second line.\n\nA paragraph after a blank line.",
            details.Description);
        Assert.Equal("Fixed a crash on startup.\n- Faster search\n- Smaller download", details.ReleaseNotes);
        Assert.Equal("https://example.com/", details.Homepage);
        Assert.Empty(details.AdditionalFields);
    }

    [Fact]
    public void Parse_NoMatchOutput_ReturnsNull()
    {
        Assert.Null(WingetShowParser.Parse(NoMatchOutput));
    }
}
