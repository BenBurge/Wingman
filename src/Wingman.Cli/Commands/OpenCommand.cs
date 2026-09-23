namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman open &lt;route&gt;</c>: opens the TUI on a tab. It runs in this console when there is
/// one; started by a toast button or the tray without a console, it opens a new terminal window
/// running <c>open &lt;route&gt; --attached</c> and exits.
/// </summary>
internal sealed class OpenCommand : ICliCommand
{
    private const string ProtocolPrefix = "wingman:";

    /// <summary>
    /// Every route <c>wingman open</c> accepts. <c>wt.exe</c> splits its command line on
    /// <c>;</c>, so an unvalidated route could inject a second command; anything not in this set
    /// is rejected before a process is spawned or the TUI is launched.
    /// </summary>
    internal static readonly IReadOnlySet<string> Routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "updates", "update-all", "history", "settings", "installed", "discover",
    };

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
        if (route is not null && !Routes.Contains(route))
        {
            context.Error.WriteLine($"wingman open: unknown route '{route}'");
            return ExitCodes.Usage;
        }

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

        var argv = TerminalArgv(context.ExePath, route, context.IsFake, ResolveWindowsTerminal());
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
    internal static string[] TerminalArgv(string exePath, string? route, bool isFake, string? windowsTerminalPath)
    {
        var argv = new List<string>();
        if (windowsTerminalPath is not null)
        {
            argv.AddRange([windowsTerminalPath, "-w", "0", "nt"]);
        }
        else
        {
            argv.Add(ConhostPath());
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

    // conhost.exe hosts a console for a program that has none; resolving it from the system
    // directory instead of trusting a bare name keeps a PATH entry ahead of System32 from
    // substituting a different binary.
    private static string ConhostPath() => Path.Combine(Environment.SystemDirectory, "conhost.exe");

    // The same lookup as `where wt`, without starting a process. Windows Terminal installs wt.exe
    // as an app execution alias under WindowsApps, which is normally on PATH. Some hardened
    // environments strip user PATH entries the alias depends on, so fall back to the fixed
    // location it always installs to.
    private static string? ResolveWindowsTerminal()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "wt.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(localAppData))
        {
            return null;
        }

        var fallback = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
