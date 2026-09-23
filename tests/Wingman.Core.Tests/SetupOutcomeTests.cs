using Wingman.Core.Setup;

namespace Wingman.Core.Tests;

public class SetupOutcomeTests
{
    [Theory]
    [InlineData(false, false, SetupResult.Created)]
    [InlineData(false, true, SetupResult.WouldCreate)]
    [InlineData(true, false, SetupResult.Updated)]
    [InlineData(true, true, SetupResult.WouldCreate)]
    public void Decide_EnabledPartThatDiffers_IsCreatedOrUpdated(bool exists, bool dryRun, string expected)
    {
        var outcome = SetupOutcomes.Decide(exists, enabled: true, remove: false, dryRun, upToDate: false);

        Assert.Equal(expected, outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_EnabledPartAlreadyUpToDate_IsUnchanged(bool dryRun)
    {
        var outcome = SetupOutcomes.Decide(exists: true, enabled: true, remove: false, dryRun, upToDate: true);

        Assert.Equal(SetupResult.Unchanged, outcome);
    }

    [Fact]
    public void Decide_UpToDateIsIgnoredForAMissingPart()
    {
        var outcome = SetupOutcomes.Decide(exists: false, enabled: true, remove: false, dryRun: false, upToDate: true);

        Assert.Equal(SetupResult.Created, outcome);
    }

    [Theory]
    [InlineData(true, false, SetupResult.Removed)]
    [InlineData(true, true, SetupResult.WouldRemove)]
    [InlineData(false, false, SetupResult.Unchanged)]
    [InlineData(false, true, SetupResult.Unchanged)]
    public void Decide_DisabledPart_IsRemovedIfPresentWithoutRemoveFlag(bool exists, bool dryRun, string expected)
    {
        var outcome = SetupOutcomes.Decide(exists, enabled: false, remove: false, dryRun, upToDate: true);

        Assert.Equal(expected, outcome);
    }

    [Theory]
    [InlineData(true, true, false, SetupResult.Removed)]
    [InlineData(true, false, false, SetupResult.Removed)]
    [InlineData(true, true, true, SetupResult.WouldRemove)]
    [InlineData(false, true, false, SetupResult.Unchanged)]
    [InlineData(false, false, true, SetupResult.Unchanged)]
    public void Decide_Remove_RemovesWhatExistsWhateverTheSetting(bool exists, bool enabled, bool dryRun, string expected)
    {
        var outcome = SetupOutcomes.Decide(exists, enabled, remove: true, dryRun, upToDate: true);

        Assert.Equal(expected, outcome);
    }
}
