using System.Text.RegularExpressions;
using Wingman.Cli;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class CliRunnerTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));

    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Version_PrintsWingmanAndTheInformationalVersion()
    {
        var exitCode = await CliRunner.RunAsync(["--version"], _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Matches(new Regex(@"^wingman \S+\r?\n$"), _out.ToString());
        Assert.Equal($"wingman {CliRunner.Version}{Environment.NewLine}", _out.ToString());
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task VersionCommand_PrintsTheSameAsTheFlag()
    {
        var exitCode = await CliRunner.RunAsync(["version"], _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("wingman ", _out.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task Help_ListsEveryCommandWithItsSummary(string flag)
    {
        var exitCode = await CliRunner.RunAsync([flag], _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        var text = _out.ToString();
        foreach (var command in CliCommands.All)
        {
            Assert.Matches(new Regex($@"(?m)^  {Regex.Escape(command.Name)} +{Regex.Escape(command.Summary)}\r?$"), text);
        }

        Assert.Contains("Global options: --fake  --json  --help  --version", text);
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public async Task Help_AlignsSummariesInOneColumn()
    {
        await CliRunner.RunAsync(["--help"], _out, _error, CancellationToken.None);

        var summaryColumns = new HashSet<int>();
        foreach (var command in CliCommands.All)
        {
            var line = _out.ToString().Split(Environment.NewLine).Single(l => l.StartsWith($"  {command.Name} "));
            summaryColumns.Add(line.IndexOf(command.Summary, StringComparison.Ordinal));
        }

        Assert.Single(summaryColumns);
    }

    [Fact]
    public void Registry_HoldsEveryPhase3Command()
    {
        string[] expected =
        [
            "check", "list", "search", "upgrade", "install", "export",
            "import", "history", "setup", "self-update", "tray", "open",
        ];

        Assert.Equal(expected, CliCommands.All.Select(c => c.Name));
    }

    [Fact]
    public async Task HelpCheck_PrintsTheCommandsUsage()
    {
        var check = CliCommands.All.Single(c => c.Name == "check");

        var exitCode = await CliRunner.RunAsync(["help", "check"], _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains(check.Summary, _out.ToString());
        Assert.Contains(check.Usage, _out.ToString());
        Assert.DoesNotContain("Commands:", _out.ToString());
    }

    [Fact]
    public async Task CommandWithHelpFlag_PrintsTheCommandsUsageWithoutRunningIt()
    {
        var command = new ThrowingCommand(new InvalidOperationException("must not run"));

        var exitCode = await RunAsync(["boom", "--help"], command);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains(command.Usage, _out.ToString());
        Assert.Empty(_error.ToString());
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("help", "nope")]
    [InlineData("nope", "--help")]
    public async Task UnknownCommand_ReturnsUsageAndWritesToError(params string[] args)
    {
        var exitCode = await CliRunner.RunAsync(args, _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.StartsWith($"wingman: unknown command 'nope'{Environment.NewLine}", _error.ToString());
        Assert.Contains("Commands:", _error.ToString());
        Assert.Empty(_out.ToString());
    }

    [Fact]
    public async Task NoCommand_PrintsUsageToErrorAndReturnsUsage()
    {
        var exitCode = await CliRunner.RunAsync(["--json"], _out, _error, CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("Commands:", _error.ToString());
    }

    [Fact]
    public async Task CommandThatThrows_ReturnsFailureWithTheMessage()
    {
        var exitCode = await RunAsync(["boom"], new ThrowingCommand(new InvalidOperationException("the disk is full")));

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Equal($"wingman: the disk is full{Environment.NewLine}", _error.ToString());
    }

    [Fact]
    public async Task CommandThatIsCanceled_ReturnsCanceled()
    {
        var exitCode = await RunAsync(["boom"], new ThrowingCommand(new OperationCanceledException()));

        Assert.Equal(ExitCodes.Canceled, exitCode);
    }

    [Fact]
    public async Task Dispatch_PassesParsedArgsAndContextToTheCommand()
    {
        var command = new RecordingCommand();

        var exitCode = await RunAsync(["BOOM", "Git.Git", "--fake", "--json"], command);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(["Git.Git"], command.Args!.Positionals);
        Assert.IsType<FakeWingetClient>(command.Context!.Client);
        Assert.True(command.Context.IsFake);
        Assert.True(command.Context.Json);
        Assert.False(command.Context.UseColor);
        Assert.Equal(_dataDirectory, command.Context.DataDirectory);
        Assert.Equal(Path.Combine(_dataDirectory, "history"), command.Context.History.DirectoryPath);
    }

    [Fact]
    public async Task Placeholder_ReportsNotImplementedAndReturnsUsage()
    {
        var tray = CliCommands.All.Single(c => c.Name == "tray");

        var exitCode = await RunAsync(["tray", "--fake"], tray);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal(
            $"wingman tray: not implemented yet (see the Phase 3 issues){Environment.NewLine}",
            _error.ToString());
    }

    [Fact]
    public async Task Dispatch_ReparsesWithTheCommandsFlags()
    {
        var command = new RecordingCommand();

        await RunAsync(["boom", "--yes", "Git.Git", "--last", "5"], command);

        Assert.Equal(["Git.Git"], command.Args!.Positionals);
        Assert.Equal("", command.Args.GetOption("yes"));
        Assert.Equal("5", command.Args.GetOption("last"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(false, "--fake")]
    [InlineData(true, "check")]
    [InlineData(true, "--fake", "check")]
    [InlineData(true, "--help")]
    [InlineData(true, "-h")]
    [InlineData(true, "--version")]
    public void IsHeadless_TrueForACommandHelpOrVersion(bool expected, params string[] args)
    {
        Assert.Equal(expected, CliRunner.IsHeadless(args));
    }

    private Task<int> RunAsync(string[] args, ICliCommand command) =>
        CliRunner.RunAsync(
            args,
            [command],
            parsed => CliContext.Create(parsed, _out, _error, _dataDirectory, CancellationToken.None),
            _out,
            _error);

    private sealed class ThrowingCommand(Exception exception) : ICliCommand
    {
        public string Name => "boom";

        public string Summary => "Always throws";

        public string Usage => "Usage: wingman boom";

        public Task<int> RunAsync(CliArgs args, CliContext context) => throw exception;
    }

    private sealed class RecordingCommand : ICliCommand
    {
        public CliArgs? Args { get; private set; }

        public CliContext? Context { get; private set; }

        public string Name => "boom";

        public string Summary => "Records what it was given";

        public string Usage => "Usage: wingman boom";

        public IReadOnlyCollection<string> Flags => ["yes"];

        public Task<int> RunAsync(CliArgs args, CliContext context)
        {
            Args = args;
            Context = context;
            return Task.FromResult(ExitCodes.Success);
        }
    }
}
