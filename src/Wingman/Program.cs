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

var isFake = args.Contains("--fake");
var unrecognizedArgs = args.Where(a => a != "--fake").ToArray();

if (unrecognizedArgs.Length > 0)
{
    Console.Error.WriteLine("wingman: headless commands are not implemented yet");
    return 2;
}

IWingetClient client = isFake ? new FakeWingetClient() : new WingetCliClient(new ProcessRunner());

// The fake never needs the elevated helper, and starting it would show a real UAC prompt.
var elevation = isFake ? null : ElevationSupport.Factory;

// The fake leaves Auto on the dark theme, so its screens never depend on the machine's mode.
IThemeDetector? themeDetector = !isFake && OperatingSystem.IsWindows() ? new WindowsThemeDetector() : null;
WingmanApp.Run(client, elevation, themeDetector);
return 0;
