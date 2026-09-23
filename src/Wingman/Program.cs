using Wingman.Core.Winget;
using Wingman.Tui;

var isFake = args.Contains("--fake");
var unrecognizedArgs = args.Where(a => a != "--fake").ToArray();

if (unrecognizedArgs.Length > 0)
{
    Console.Error.WriteLine("wingman: headless commands are not implemented yet");
    return 2;
}

IWingetClient client = isFake ? new FakeWingetClient() : new WingetCliClient(new ProcessRunner());
WingmanApp.Run(client);
return 0;
