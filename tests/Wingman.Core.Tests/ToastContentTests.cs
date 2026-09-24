using System.Xml.Linq;
using Wingman.Core.Models;
using Wingman.Core.Notifications;

namespace Wingman.Core.Tests;

public class ToastContentTests
{
    private static PackageRow Row(string name) => new(name, $"{name}.{name}", "1.0", "2.0", "winget");

    // --- ToastBuilder.UpdatesAvailable ---

    [Fact]
    public void UpdatesAvailable_OnePackage_SingularTitleAndFullBody()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git")]);

        Assert.Equal("1 update available", content.Title);
        Assert.Equal("Git", content.Body);
        Assert.Equal("wingman-updates", content.Tag);
        Assert.Equal("wingman", content.Group);
    }

    [Fact]
    public void UpdatesAvailable_ThreePackagesOrFewer_PluralTitleAndNoTruncation()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git"), Row("7zip"), Row("VSCode")]);

        Assert.Equal("3 updates available", content.Title);
        Assert.Equal("Git, 7zip, VSCode", content.Body);
    }

    [Fact]
    public void UpdatesAvailable_MoreThanThreePackages_TruncatesToFirstThreeAndCountsRest()
    {
        var updates = new[] { Row("A"), Row("B"), Row("C"), Row("D"), Row("E") };

        var content = ToastBuilder.UpdatesAvailable(updates);

        Assert.Equal("5 updates available", content.Title);
        Assert.Equal("A, B, C and 2 more", content.Body);
    }

    [Fact]
    public void UpdatesAvailable_HasUpdateAllAndOpenActions()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git")]);

        Assert.Collection(content.Actions,
            action => Assert.Equal(new ToastAction("Update all", "wingman:update-all"), action),
            action => Assert.Equal(new ToastAction("Open", "wingman:updates"), action));
    }

    // --- ToastBuilder.BatchFinished ---

    [Fact]
    public void BatchFinished_AllSucceeded_TitleReportsSuccessCount()
    {
        var content = ToastBuilder.BatchFinished(total: 3, succeeded: 3, failed: 0, canceled: 0);

        Assert.Equal("3 of 3 updated", content.Title);
        Assert.Equal("wingman-batch", content.Tag);
    }

    [Fact]
    public void BatchFinished_AnyFailed_TitleReportsFailureCount()
    {
        var content = ToastBuilder.BatchFinished(total: 3, succeeded: 1, failed: 1, canceled: 1);

        Assert.Equal("1 of 3 failed", content.Title);
        Assert.Contains("1 succeeded", content.Body);
        Assert.Contains("1 failed", content.Body);
        Assert.Contains("1 canceled", content.Body);
    }

    [Fact]
    public void BatchFinished_CanceledWithNoFailures_TitleIsBatchCanceled()
    {
        var content = ToastBuilder.BatchFinished(total: 3, succeeded: 1, failed: 0, canceled: 2);

        Assert.Equal("Batch canceled", content.Title);
    }

    [Fact]
    public void BatchFinished_HasViewLogAction()
    {
        var content = ToastBuilder.BatchFinished(total: 1, succeeded: 1, failed: 0, canceled: 0);

        var action = Assert.Single(content.Actions);
        Assert.Equal(new ToastAction("View log", "wingman:history"), action);
    }

    // --- ToastBuilder.ToXml ---

    [Fact]
    public void ToXml_ProducesWellFormedXmlWithOneActionPerAction()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git"), Row("7zip")]);

        var xml = ToastBuilder.ToXml(content);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root);
        Assert.Equal("toast", doc.Root.Name.LocalName);
        Assert.Equal("wingman:update-all", doc.Root.Attribute("launch")?.Value);

        var actionElements = doc.Root.Element("actions")!.Elements("action").ToList();
        Assert.Equal(content.Actions.Count, actionElements.Count);
        for (var i = 0; i < content.Actions.Count; i++)
        {
            Assert.Equal(content.Actions[i].Content, actionElements[i].Attribute("content")?.Value);
            Assert.Equal(content.Actions[i].ProtocolUri, actionElements[i].Attribute("arguments")?.Value);
            Assert.Equal("protocol", actionElements[i].Attribute("activationType")?.Value);
        }

        var textElements = doc.Root.Element("visual")!.Element("binding")!.Elements("text").ToList();
        Assert.Equal(content.Title, textElements[0].Value);
        Assert.Equal(content.Body, textElements[1].Value);
    }

    [Fact]
    public void ToXml_EscapesAmpersandAndLessThanInPackageName()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Foo & <Bar>")]);

        var xml = ToastBuilder.ToXml(content);

        Assert.Contains("Foo &amp; &lt;Bar&gt;", xml);

        var doc = XDocument.Parse(xml);
        var body = doc.Root!.Element("visual")!.Element("binding")!.Elements("text").Last().Value;
        Assert.Equal("Foo & <Bar>", body);
    }

    [Fact]
    public void ToXml_WithNoActions_FallsBackToDefaultLaunchUri()
    {
        var content = new ToastContent("Title", "Body", [], "tag", "group");

        var xml = ToastBuilder.ToXml(content);
        var doc = XDocument.Parse(xml);

        Assert.Equal("wingman:updates", doc.Root!.Attribute("launch")?.Value);
        Assert.Empty(doc.Root.Element("actions")!.Elements("action"));
    }

    // --- ToastBuilder.BuildPowerShellCommand ---

    [Fact]
    public void BuildPowerShellCommand_StartsWithHiddenNonInteractivePowerShellInvocation()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git")]);

        var argv = ToastBuilder.BuildPowerShellCommand(content, "BenBurge.Wingman");

        Assert.Equal("powershell.exe", argv[0]);
        Assert.Contains("-NoProfile", argv);
        Assert.Contains("-NonInteractive", argv);
        Assert.Contains("-WindowStyle", argv);
        Assert.Contains("Hidden", argv);
        Assert.Equal("-Command", argv[^2]);
    }

    [Fact]
    public void BuildPowerShellCommand_ContainsAppIdAndEscapedXml()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git")]);
        var xml = ToastBuilder.ToXml(content);

        var argv = ToastBuilder.BuildPowerShellCommand(content, "BenBurge.Wingman");
        var script = argv[^1];

        Assert.Contains("BenBurge.Wingman", script);
        Assert.Contains(xml.Replace("'", "''"), script);
        Assert.Contains("ToastNotificationManager", script);
        Assert.Contains(".Tag = ", script);
        Assert.Contains(".Group = ", script);
    }

    [Fact]
    public void BuildPowerShellCommand_HasBalancedSingleQuotes()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("Git")]);

        var argv = ToastBuilder.BuildPowerShellCommand(content, "BenBurge.Wingman");
        var script = argv[^1];

        var quoteCount = script.Count(c => c == '\'');
        Assert.Equal(0, quoteCount % 2);
    }

    // --- ToastBuilder.WingmanUpdated ---

    [Fact]
    public void WingmanUpdated_OpensTheReleasePageFromTheToastAndItsAction()
    {
        var content = ToastBuilder.WingmanUpdated("1.2.0");

        Assert.Equal("Wingman updated to v1.2.0", content.Title);
        Assert.Equal("wingman-self-update", content.Tag);
        var action = Assert.Single(content.Actions);
        Assert.Equal(new ToastAction("What's new", "https://github.com/BenBurge/Wingman/releases/tag/v1.2.0"), action);
        var xml = XElement.Parse(ToastBuilder.ToXml(content));
        Assert.Equal("https://github.com/BenBurge/Wingman/releases/tag/v1.2.0", xml.Attribute("launch")?.Value);
        Assert.Equal("protocol", xml.Attribute("activationType")?.Value);
    }

    [Fact]
    public void BuildPowerShellCommand_PackageNameWithSingleQuote_KeepsQuotesBalanced()
    {
        var content = ToastBuilder.UpdatesAvailable([Row("O'Brien's Tool")]);

        var argv = ToastBuilder.BuildPowerShellCommand(content, "BenBurge.Wingman");
        var script = argv[^1];

        var quoteCount = script.Count(c => c == '\'');
        Assert.Equal(0, quoteCount % 2);
    }
}
