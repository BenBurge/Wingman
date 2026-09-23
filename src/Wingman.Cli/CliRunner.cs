using System.Reflection;

namespace Wingman.Cli;

/// <summary>Parses a command line and runs the headless command it names.</summary>
public static class CliRunner
{
    private const string DevelopmentVersion = "0.0.0-dev";

    /// <summary>
    /// The entry assembly's informational version, which the release build stamps; <c>0.0.0-dev</c>
    /// when it carries none.
    /// </summary>
    internal static string Version
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(CliRunner).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(version) ? DevelopmentVersion : version;
        }
    }

    /// <summary>
    /// True when the arguments name a command or ask for help or the version, so the host runs
    /// <see cref="RunAsync(string[], TextWriter, TextWriter, CancellationToken)"/> instead of the TUI.
    /// </summary>
    public static bool IsHeadless(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        return parsed.Command is not null || parsed.HasFlag("help") || parsed.HasFlag("version");
    }

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct) =>
        RunAsync(args, CliCommands.All, parsed => CliContext.Create(parsed, output, error, ct), output, error);

    /// <summary>Runs against the given commands and context factory so tests can supply their own.</summary>
    internal static async Task<int> RunAsync(
        string[] args,
        IReadOnlyList<ICliCommand> commands,
        Func<CliArgs, CliContext> createContext,
        TextWriter output,
        TextWriter error)
    {
        var parsed = CliArgs.Parse(args);

        // With a command, --version belongs to it (install takes --version <v>).
        var wantsVersion = IsCommand(parsed, "version") || (parsed.Command is null && parsed.HasFlag("version"));
        if (wantsVersion)
        {
            output.WriteLine($"wingman {Version}");
            return ExitCodes.Success;
        }

        if (IsCommand(parsed, "help"))
        {
            if (parsed.Positionals.Count == 0)
            {
                WriteUsage(output, commands);
                return ExitCodes.Success;
            }

            return WriteCommandHelp(parsed.Positionals[0], commands, output, error);
        }

        if (parsed.Command is null)
        {
            if (parsed.HasFlag("help"))
            {
                WriteUsage(output, commands);
                return ExitCodes.Success;
            }

            WriteUsage(error, commands);
            return ExitCodes.Usage;
        }

        if (parsed.HasFlag("help"))
        {
            return WriteCommandHelp(parsed.Command, commands, output, error);
        }

        var command = Find(commands, parsed.Command);
        if (command is null)
        {
            WriteUnknownCommand(parsed.Command, commands, error);
            return ExitCodes.Usage;
        }

        // The first parse only has to find the command; its own flags decide which options take values.
        var commandArgs = CliArgs.Parse(args, command.Flags);

        try
        {
            var context = createContext(commandArgs);
            return await command.RunAsync(commandArgs, context);
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Canceled;
        }
        catch (Exception ex)
        {
            error.WriteLine($"wingman: {ex.Message}");
            return ExitCodes.Failure;
        }
    }

    private static bool IsCommand(CliArgs args, string name) =>
        string.Equals(args.Command, name, StringComparison.OrdinalIgnoreCase);

    private static ICliCommand? Find(IReadOnlyList<ICliCommand> commands, string name)
    {
        foreach (var command in commands)
        {
            if (string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }
        }

        return null;
    }

    private static int WriteCommandHelp(
        string name, IReadOnlyList<ICliCommand> commands, TextWriter output, TextWriter error)
    {
        var command = Find(commands, name);
        if (command is null)
        {
            WriteUnknownCommand(name, commands, error);
            return ExitCodes.Usage;
        }

        output.WriteLine(command.Summary);
        output.WriteLine();
        output.WriteLine(command.Usage);
        return ExitCodes.Success;
    }

    private static void WriteUnknownCommand(string name, IReadOnlyList<ICliCommand> commands, TextWriter error)
    {
        error.WriteLine($"wingman: unknown command '{name}'");
        error.WriteLine();
        WriteUsage(error, commands);
    }

    private static void WriteUsage(TextWriter writer, IReadOnlyList<ICliCommand> commands)
    {
        var nameWidth = 0;
        foreach (var command in commands)
        {
            nameWidth = Math.Max(nameWidth, command.Name.Length);
        }

        writer.WriteLine("Usage: wingman [command] [options]");
        writer.WriteLine();
        writer.WriteLine("With no command, wingman opens the terminal UI.");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        foreach (var command in commands)
        {
            writer.WriteLine($"  {command.Name.PadRight(nameWidth)}  {command.Summary}");
        }

        writer.WriteLine();
        writer.WriteLine("Global options: --fake  --json  --help  --version");
        writer.WriteLine();
        writer.WriteLine("Run 'wingman help <command>' for a command's usage.");
    }
}
