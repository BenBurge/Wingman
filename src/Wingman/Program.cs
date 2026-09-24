using System.Runtime.InteropServices;
using System.Text;
using Wingman.Cli;
using Wingman.Core.Elevation;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui;
using Wingman.Windows;
using Wingman.Windows.Elevation;
using Wingman.Windows.Tray;

// The elevated helper is this same executable relaunched by ElevatedHelperLauncher, so its
// arguments are handled before anything that would start the TUI.
if (args is ["--elevated-worker", ..])
{
    if (!ElevatedWorkerArguments.TryParse(args, out var pipeName, out var parentPid))
    {
        Console.Error.WriteLine($"wingman: usage: wingman {ElevatedWorkerArguments.Usage}");
        return ExitCodes.Usage;
    }

    if (OperatingSystem.IsWindows())
    {
        return await ElevatedWorker.RunAsync(pipeName, parentPid, CancellationToken.None);
    }

    Console.Error.WriteLine("elevated worker is Windows-only");
    return 2;
}

var isFake = args.Contains("--fake");

// One client for the whole process, as HttpClient is meant to be shared. The timeout covers each
// request up to its response headers, so a large installer download is not cut off by it.
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var rid = ReleaseRid(RuntimeInformation.RuntimeIdentifier);

if (CliRunner.IsHeadless(args))
{
    UseUtf8Output();

    using var cancel = new CancellationTokenSource();

    // The first Ctrl+C cancels the command so it can stop cleanly; a second one ends the process.
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = !cancel.IsCancellationRequested;
        cancel.Cancel();
    };

    var processRunner = new ProcessRunner();
    var services = new CliHostServices
    {
        SetupExecutor = HostServices.SetupExecutor(processRunner),
        ToastSender = HostServices.ToastSender(processRunner),
        SelfUpdateStarter = HostServices.SelfUpdateStarter(),
        ReleaseSource = new GitHubReleaseSource(http),
        UpdateDownloader = new UpdateDownloader(http),
        InstallerRegisteredFolder = HostServices.InstallerRegisteredFolder(),
        Rid = rid,
        TrayRunner = TrayHost.Run,
        TuiLauncher = (route, _) => Task.FromResult(RunTui(isFake, route, http, rid)),
        WindowSpawner = HostServices.WindowSpawner,
        ElevationFactory = ElevationSupport.Factory,
        ProcessIsElevated = ElevationSupport.IsElevated ?? false,
        HasConsole = HostServices.HasConsoleWindow ?? true,
    };

    return await CliRunner.RunAsync(args, Console.Out, Console.Error, cancel.Token, services);
}

var unrecognizedArgs = args.Where(a => a != "--fake").ToArray();

if (unrecognizedArgs.Length > 0)
{
    Console.Error.WriteLine($"wingman: unexpected argument '{unrecognizedArgs[0]}'; run 'wingman --help'");
    return ExitCodes.Usage;
}

return RunTui(isFake, startRoute: null, http, rid);

static int RunTui(bool isFake, string? startRoute, HttpClient http, string rid)
{
    IWingetClient client = isFake ? new FakeWingetClient() : new WingetCliClient(new ProcessRunner());

    // The fake never needs the elevated helper or a restart, and either would show a real UAC prompt.
    var elevation = isFake ? null : ElevationSupport.Factory;
    var restartAsAdministrator = isFake ? null : ElevationSupport.RestartAsAdministrator;
    var isElevated = !isFake && (ElevationSupport.IsElevated ?? false);

    // The fake leaves Auto on the dark theme, so its screens never depend on the machine's mode.
    IThemeDetector? themeDetector = !isFake && OperatingSystem.IsWindows() ? new WindowsThemeDetector() : null;

    // Setup, self-update, and toasts from the fake would touch the real machine.
    ShellServices services;
    if (isFake)
    {
        services = new ShellServices(null, null, null, CliRunner.Version);
    }
    else
    {
        var runner = new ProcessRunner();
        services = new ShellServices(
            HostServices.SetupExecutor(runner), HostServices.SelfUpdateStarter(), HostServices.ToastSender(runner), CliRunner.Version)
        {
            Releases = new GitHubReleaseSource(http),
            Downloader = new UpdateDownloader(http),
            Rid = rid,
        };
    }

    WingmanApp.Run(client, elevation, themeDetector, isElevated, restartAsAdministrator, services, startRoute);
    return 0;
}

// Releases publish one installer per architecture; anything that is not Arm64 runs the x64 one,
// which Windows on Arm can also emulate.
static string ReleaseRid(string runtimeIdentifier) =>
    runtimeIdentifier.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase) ? "win-arm64" : "win-x64";

// Windows consoles start on the OEM code page, which cannot print the ✓ ✗ ⊘ ⚡ markers the
// commands use. Setting it fails when the process has no console at all, and then nothing reads
// the output anyway.
static void UseUtf8Output()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
    }
}
