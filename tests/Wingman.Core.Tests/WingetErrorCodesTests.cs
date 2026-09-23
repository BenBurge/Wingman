using Wingman.Core.Updates;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class WingetErrorCodesTests
{
    public static IEnumerable<object[]> KnownCodes => new[]
    {
        new object[] { -1978335231, "INTERNAL_ERROR" },
        new object[] { -1978335230, "INVALID_CL_ARGUMENTS" },
        new object[] { -1978335226, "SHELLEXEC_INSTALL_FAILED" },
        new object[] { -1978335224, "DOWNLOAD_FAILED" },
        new object[] { -1978335216, "NO_APPLICABLE_INSTALLER" },
        new object[] { -1978335215, "INSTALLER_HASH_MISMATCH" },
        new object[] { -1978335212, "NO_APPLICATIONS_FOUND" },
        new object[] { -1978335191, "MANIFEST_VALIDATION_FAILURE" },
        new object[] { -1978335189, "UPDATE_NOT_APPLICABLE" },
        new object[] { -1978335188, "UPDATE_ALL_HAS_FAILURE" },
        new object[] { -1978335135, "PACKAGE_ALREADY_INSTALLED" },
        new object[] { -1978335134, "PIN_ALREADY_EXISTS" },
        new object[] { -1978335133, "PIN_DOES_NOT_EXIST" },
        new object[] { -1978335128, "PACKAGE_IS_PINNED" },
        new object[] { -1978335127, "PACKAGE_IS_STUB" },
        new object[] { -1978334975, "INSTALL_PACKAGE_IN_USE" },
        new object[] { -1978334974, "INSTALL_INSTALL_IN_PROGRESS" },
        new object[] { -1978334972, "INSTALL_MISSING_DEPENDENCY" },
        new object[] { -1978334971, "INSTALL_DISK_FULL" },
        new object[] { -1978334970, "INSTALL_INSUFFICIENT_MEMORY" },
        new object[] { -1978334969, "INSTALL_NO_NETWORK" },
        new object[] { -1978334968, "INSTALL_CONTACT_SUPPORT" },
        new object[] { -1978334967, "INSTALL_REBOOT_REQUIRED_TO_FINISH" },
        new object[] { -1978334966, "INSTALL_REBOOT_REQUIRED_FOR_INSTALL" },
        new object[] { -1978334965, "INSTALL_REBOOT_INITIATED" },
        new object[] { -1978334964, "INSTALL_CANCELLED_BY_USER" },
        new object[] { -1978334963, "INSTALL_ALREADY_INSTALLED" },
        new object[] { -1978334962, "INSTALL_DOWNGRADE" },
        new object[] { -1978334961, "INSTALL_BLOCKED_BY_POLICY" },
        new object[] { -1978334960, "INSTALL_DEPENDENCIES" },
        new object[] { -1978334959, "INSTALL_PACKAGE_IN_USE_BY_APPLICATION" },
        new object[] { -1978334958, "INSTALL_INVALID_PARAMETER" },
        new object[] { -1978334957, "INSTALL_SYSTEM_NOT_SUPPORTED" },
        new object[] { -1978334956, "INSTALL_UPGRADE_NOT_SUPPORTED" },
        new object[] { -1978334955, "INSTALL_CUSTOM_ERROR" },
        new object[] { -2147024228, "ERROR_ASSERTION_FAILURE" },
        new object[] { -2147024891, "E_ACCESSDENIED" },
        new object[] { 1602, "ERROR_INSTALL_USEREXIT" },
        new object[] { 1603, "ERROR_INSTALL_FAILURE" },
        new object[] { 1618, "ERROR_INSTALL_ALREADY_RUNNING" },
        new object[] { 1619, "ERROR_INSTALL_PACKAGE_OPEN_FAILED" },
        new object[] { 1638, "ERROR_PRODUCT_VERSION" },
        new object[] { 3010, "ERROR_SUCCESS_REBOOT_REQUIRED" },
    };

    [Theory]
    [MemberData(nameof(KnownCodes))]
    public void Explain_KnownCode_ReturnsItsName(int exitCode, string expectedName)
    {
        var explanation = WingetErrorCodes.Explain(exitCode);

        Assert.Equal(expectedName, explanation.Name);
    }

    [Fact]
    public void Format_NegativeHResult_RendersAsUppercaseHex()
    {
        Assert.Equal("0x8A150011", WingetErrorCodes.Format(-1978335215));
    }

    [Fact]
    public void Format_PositiveMsiCode_RendersAsDecimal()
    {
        Assert.Equal("1603", WingetErrorCodes.Format(1603));
    }

    [Fact]
    public void Format_Zero_RendersAsZero()
    {
        Assert.Equal("0", WingetErrorCodes.Format(0));
    }

    // tests/Wingman.Core.Tests/Fixtures/exit-codes.txt captures this exact code from a real
    // "winget upgrade" run with no matching package (search-nomatch), so its hex is pinned here
    // rather than only trusted to the header lookup above.
    [Fact]
    public void Format_FixtureObservedNoApplicationsFound_MatchesCapturedHex()
    {
        Assert.Equal("0x8A150014", WingetErrorCodes.Format(-1978335212));
    }

    [Fact]
    public void Format_FixtureObservedInvalidClArguments_MatchesCapturedHex()
    {
        Assert.Equal("0x8A150002", WingetErrorCodes.Format(-1978335230));
    }

    [Fact]
    public void Explain_HashMismatch_SuggestsExcludingThePackage()
    {
        var explanation = WingetErrorCodes.Explain(-1978335215);

        Assert.Equal(UpdatePolicyKind.Exclude, explanation.SuggestedPolicy);
    }

    [Fact]
    public void Explain_ProductVersion_SuggestsExcludingThePackage()
    {
        var explanation = WingetErrorCodes.Explain(1638);

        Assert.Equal(UpdatePolicyKind.Exclude, explanation.SuggestedPolicy);
    }

    [Fact]
    public void Explain_PackageInUse_SuggestsClosingTheApp()
    {
        var explanation = WingetErrorCodes.Explain(-1978334975);

        Assert.Contains("Close the app", explanation.Suggestion);
    }

    [Fact]
    public void Explain_PackageInUseByApplication_SuggestsClosingTheApp()
    {
        var explanation = WingetErrorCodes.Explain(-1978334959);

        Assert.Contains("Close the app", explanation.Suggestion);
    }

    [Fact]
    public void Explain_PackageIsPinned_SuggestsReleasingTheHold()
    {
        var explanation = WingetErrorCodes.Explain(-1978335128);

        Assert.Contains("Release the hold", explanation.Suggestion);
    }

    [Theory]
    [InlineData(-2147024228, "0x8007029C")]
    [InlineData(-2147024891, "0x80070005")]
    public void Explain_ElevationFailure_SuggestsRetryingElevatedWithNoPolicy(int exitCode, string expectedHex)
    {
        var explanation = WingetErrorCodes.Explain(exitCode);

        Assert.Equal(expectedHex, explanation.Code);
        Assert.StartsWith("Retry elevated", explanation.Suggestion);
        Assert.Null(explanation.SuggestedPolicy);
        Assert.True(WingetErrorCodes.SuggestsElevation(exitCode));
    }

    [Fact]
    public void Explain_AssertionFailure_NamesTheInterceptedPrompt()
    {
        var explanation = WingetErrorCodes.Explain(-2147024228);

        Assert.Equal("An assertion failure has occurred.", explanation.WingetSaid);
        Assert.Contains("Admin By Request", explanation.UsuallyMeans);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1603)]
    [InlineData(-1978334964)]
    [InlineData(424242)]
    public void SuggestsElevation_OtherCodes_ReturnsFalse(int exitCode)
    {
        Assert.False(WingetErrorCodes.SuggestsElevation(exitCode));
    }

    [Fact]
    public void Explain_UnknownCode_ReturnsUnknownWithTheCodeInUsuallyMeans()
    {
        var explanation = WingetErrorCodes.Explain(424242);

        Assert.Equal("Unknown", explanation.Name);
        Assert.Contains("424242", explanation.UsuallyMeans);
        Assert.Null(explanation.SuggestedPolicy);
    }

    [Fact]
    public void Explain_RebootRequired_IsNamedAsASuccess()
    {
        var explanation = WingetErrorCodes.Explain(3010);

        Assert.Equal("ERROR_SUCCESS_REBOOT_REQUIRED", explanation.Name);
        Assert.Contains("reboot", explanation.UsuallyMeans, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_UpdateNotApplicable_PointsAtTheDowngradePath()
    {
        var explanation = WingetErrorCodes.Explain(-1978335189);

        Assert.Equal(
            "Retry with the exact version, or exclude it. To install an older version, pick it from Upgrade to version…; Wingman then runs install --force.",
            explanation.Suggestion);
    }
}
