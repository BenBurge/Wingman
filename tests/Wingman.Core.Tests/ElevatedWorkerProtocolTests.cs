using Wingman.Core.Elevation;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Tests;

public class ElevatedWorkerProtocolTests
{
    [Fact]
    public void RunOperation_WithEveryRequestField_RoundTrips()
    {
        var request = new OperationRequest(
            "Microsoft.PowerToys",
            Version: "0.81.0",
            Scope: "machine",
            Architecture: "x64",
            Interactive: true,
            SkipHashCheck: true,
            Force: true,
            CustomArguments: ["--override", "/quiet INSTALLDIR=\"C:\\Program Files\\PT\"", "line\nbreak"]);

        var line = ElevatedWorkerProtocol.Serialize(new WorkerMessage.RunOperation(OperationKind.Upgrade, request));
        var parsed = Assert.IsType<WorkerMessage.RunOperation>(ElevatedWorkerProtocol.Parse(line));

        Assert.Equal(OperationKind.Upgrade, parsed.Kind);
        Assert.Equal(request.CustomArguments, parsed.Request.CustomArguments);
        // Records compare the argument list by reference, so compare the rest with it swapped in.
        Assert.Equal(request, parsed.Request with { CustomArguments = request.CustomArguments });
    }

    [Fact]
    public void RunOperation_WithDefaultRequest_RoundTrips()
    {
        var line = ElevatedWorkerProtocol.Serialize(
            new WorkerMessage.RunOperation(OperationKind.Uninstall, new OperationRequest("Git.Git")));

        var parsed = Assert.IsType<WorkerMessage.RunOperation>(ElevatedWorkerProtocol.Parse(line));

        Assert.Equal(OperationKind.Uninstall, parsed.Kind);
        Assert.Equal(new OperationRequest("Git.Git"), parsed.Request);
    }

    [Fact]
    public void RunOperation_Serialize_UsesCamelCaseAndTypeDiscriminator()
    {
        var line = ElevatedWorkerProtocol.Serialize(
            new WorkerMessage.RunOperation(OperationKind.Install, new OperationRequest("Git.Git", SkipHashCheck: true)));

        Assert.StartsWith("{\"type\":\"run\"", line);
        Assert.Contains("\"kind\":\"install\"", line);
        Assert.Contains("\"skipHashCheck\":true", line);
    }

    [Theory]
    [MemberData(nameof(OtherMessages))]
    public void OtherMessages_RoundTrip(WorkerMessage message)
    {
        var line = ElevatedWorkerProtocol.Serialize(message);

        Assert.Equal(message, ElevatedWorkerProtocol.Parse(line));
    }

    public static TheoryData<WorkerMessage> OtherMessages() =>
    [
        new WorkerMessage.Line("Successfully installed"),
        new WorkerMessage.Line(""),
        new WorkerMessage.Line("  ██████▒▒▒▒  60%  café"),
        new WorkerMessage.Finished(0, 1234),
        new WorkerMessage.Finished(-1978335189, long.MaxValue),
        new WorkerMessage.Shutdown(),
    ];

    [Fact]
    public void Serialize_TextWithNewlines_ProducesSingleLine()
    {
        var line = ElevatedWorkerProtocol.Serialize(new WorkerMessage.Line("first\r\nsecond\nthird"));

        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.Equal(new WorkerMessage.Line("first\r\nsecond\nthird"), ElevatedWorkerProtocol.Parse(line));
    }

    [Fact]
    public void Parse_UnknownType_ThrowsFormatExceptionNamingType()
    {
        var ex = Assert.Throws<FormatException>(() => ElevatedWorkerProtocol.Parse("{\"type\":\"reboot\"}"));

        Assert.Contains("reboot", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"line\"}")]
    [InlineData("{\"type\":\"finished\",\"exitCode\":0}")]
    [InlineData("{\"type\":\"run\",\"kind\":\"install\"}")]
    [InlineData("{\"type\":\"run\",\"kind\":\"reboot\",\"request\":{\"id\":\"A\"}}")]
    public void Parse_MalformedMessage_ThrowsFormatException(string line)
    {
        Assert.Throws<FormatException>(() => ElevatedWorkerProtocol.Parse(line));
    }
}
