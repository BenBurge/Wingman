using Wingman.Cli;
using Wingman.Core.Settings;
using Wingman.Core.Winget;
using Wingman.Tui;
using Wingman.Windows;
using Wingman.Windows.Elevation;

// The elevated helper is this same executable relaunched by ElevatedHelperLauncher, so its
// arguments are handled before anything that would start the TUI.
if (args is ["--elevated-worker", var pipeName])
{
    if (OperatingSystem.IsWindows())
    {
        return await ElevatedWorker.RunAsync(pipeName, CancellationToken.None);
    }

    Console.Error.WriteLine("elevated worker is Windows-only");
    return 2;
}

if (CliRunner.IsHeadless(args))
{
    using var cancel = new CancellationTokenSource();

    // The first Ctrl+C cancels the command so it can stop cleanly; a second one ends the process.
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = !cancel.IsCancellationRequested;
        cancel.Cancel();
    };

    return await CliRunner.RunAsync(args, Console.Out, Console.Error, cancel.Token);
}

var isFake = args.Contains("--fake");
var unrecognizedArgs = args.Where(a => a != "--fake").ToArray();

if (unrecognizedArgs.Length > 0)
{
    Console.Error.WriteLine($"wingman: unexpected argument '{unrecognizedArgs[0]}'; run 'wingman --help'");
    return ExitCodes.Usage;
}

IWingetClient client = isFake ? new FakeWingetClient() : new WingetCliClient(new ProcessRunner());

// The fake never needs the elevated helper or a restart, and either would show a real UAC prompt.
var elevation = isFake ? null : ElevationSupport.Factory;
var restartAsAdministrator = isFake ? null : ElevationSupport.RestartAsAdministrator;
var isElevated = !isFake && (ElevationSupport.IsElevated ?? false);

// The fake leaves Auto on the dark theme, so its screens never depend on the machine's mode.
IThemeDetector? themeDetector = !isFake && OperatingSystem.IsWindows() ? new WindowsThemeDetector() : null;
WingmanApp.Run(client, elevation, themeDetector, isElevated, restartAsAdministrator);
return 0;
