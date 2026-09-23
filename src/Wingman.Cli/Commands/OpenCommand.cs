namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman open &lt;route&gt;</c>: opens the TUI on a tab. It runs in this console when there is
/// one; started by a toast button or the tray without a console, it opens a new terminal window
/// running <c>open &lt;route&gt; --attached</c> and exits.
/// </summary>
internal sealed class OpenCommand : ICliCommand
{
    private const string ProtocolPrefix = "wingman:";

    public string Name => "open";

    public string Summary => "Open the terminal UI on a tab";

    public string Usage => """
        Usage: wingman open [<route>] [--attached]

        Opens the terminal UI on a route: updates, update-all, installed, discover, history, or
        settings. A wingman: link such as wingman:updates works too. Without a console to draw in,
        it opens a new terminal window.

          --attached  Run in this console even when output looks redirected
        """;

    public IReadOnlyCollection<string> Flags => ["attached"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (context.TuiLauncher is not { } launch)
        {
            context.Error.WriteLine("wingman open: the terminal UI is not available in this build");
            return ExitCodes.Usage;
        }

        var route = args.Positionals.Count > 0 ? NormalizeRoute(args.Positionals[0]) : null;
        var runsHere = args.HasFlag("attached") || (!context.IsOutputRedirected && context.HasConsole);
        if (runsHere)
        {
            return await launch(route, context.Cancel);
        }

        if (context.WindowSpawner is not { } spawn)
        {
            context.Error.WriteLine("wingman open: no terminal to draw in; pass --attached to run here anyway");
            return ExitCodes.Usage;
        }

        var argv = TerminalArgv(context.ExePath, route, context.IsFake, HasWindowsTerminal());
        if (!spawn(argv))
        {
            context.Error.WriteLine($"wingman open: could not start {argv[0]}");
            return ExitCodes.Failure;
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// <c>wingman:updates</c>, <c>wingman://updates/</c>, and <c>updates</c> all name the
    /// <c>updates</c> route; the protocol handler passes the whole link. Null when nothing is left.
    /// </summary>
    internal static string? NormalizeRoute(string value)
    {
        var route = value.Trim();
        if (route.StartsWith(ProtocolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            route = route[ProtocolPrefix.Length..];
        }

        route = route.Trim('/');
        return route.Length == 0 ? null : route;
    }

    /// <summary>
    /// A new Windows Terminal tab in the most recent window (<c>wt.exe -w 0 nt</c>) when Windows
    /// Terminal is installed, else a plain console window, running <c>open &lt;route&gt; --attached</c>.
    /// </summary>
    internal static string[] TerminalArgv(string exePath, string? route, bool isFake, bool hasWindowsTerminal)
    {
        var argv = new List<string>();
        if (hasWindowsTerminal)
        {
            argv.AddRange(["wt.exe", "-w", "0", "nt"]);
        }
        else
        {
            argv.Add("conhost.exe");
        }

        argv.Add(exePath);
        argv.Add("open");
        if (route is not null)
        {
            argv.Add(route);
        }

        argv.Add("--attached");
        if (isFake)
        {
            argv.Add("--fake");
        }

        return [.. argv];
    }

    // The same lookup as `where wt`, without starting a process. Windows Terminal installs wt.exe
    // as an app execution alias under WindowsApps, which is on PATH and passes File.Exists.
    private static bool HasWindowsTerminal()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory.Trim(), "wt.exe")))
            {
                return true;
            }
        }

        return false;
    }
}
