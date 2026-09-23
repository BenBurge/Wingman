using Wingman.Cli;
using Wingman.Core.History;
using Wingman.Core.Notifications;
using Wingman.Core.Options;
using Wingman.Core.Settings;
using Wingman.Core.State;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

/// <summary>
/// Runs headless commands through <see cref="CliRunner"/> against a <see cref="FakeWingetClient"/>
/// with no step delay, stores in a temp data directory, and captured output. Stores and the client
/// are shared across runs, so a test can set up state and read it back afterward.
/// </summary>
internal sealed class CliHarness : IDisposable
{
    public CliHarness()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "wingman-tests", Guid.NewGuid().ToString("N"));
        Options = new PackageOptionsStore(DataDirectory);
        History = new HistoryStore(Path.Combine(DataDirectory, "history"));
        State = new StateStore(DataDirectory);
    }

    public string DataDirectory { get; }

    public FakeWingetClient Client { get; } = new(TimeSpan.Zero);

    public WingmanSettings Settings { get; } = new();

    public PackageOptionsStore Options { get; }

    public HistoryStore History { get; }

    public StateStore State { get; }

    public StringWriter Out { get; private set; } = new();

    public StringWriter Error { get; private set; } = new();

    public List<ToastContent> Toasts { get; } = [];

    /// <summary>False to let commands prompt; <see cref="Input"/> then answers.</summary>
    public bool IsInputRedirected { get; set; } = true;

    public string Input { get; set; } = "";

    public int Width { get; set; } = CliContext.DefaultWidth;

    public string Output => Out.ToString();

    /// <summary>Output lines without their line endings.</summary>
    public string[] OutputLines => Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Runs <paramref name="args"/> with fresh output writers, so each run's output stands alone.</summary>
    public Task<int> RunAsync(params string[] args)
    {
        Out = new StringWriter();
        Error = new StringWriter();
        return CliRunner.RunAsync(args, CliCommands.All, CreateContext, Out, Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    private CliContext CreateContext(CliArgs args) => new()
    {
        Client = Client,
        Settings = Settings,
        SettingsStore = new SettingsStore(DataDirectory),
        Options = Options,
        History = History,
        State = State,
        Out = Out,
        Error = Error,
        Input = new StringReader(Input),
        IsInputRedirected = IsInputRedirected,
        Json = args.HasFlag("json"),
        Notify = args.HasFlag("notify"),
        IsFake = true,
        Width = Width,
        Notifier = Toasts.Add,
        DataDirectory = DataDirectory,
    };
}
