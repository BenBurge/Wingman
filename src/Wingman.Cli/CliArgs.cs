namespace Wingman.Cli;

/// <summary>
/// A command line split into a command, positional arguments, and options. <c>--flag</c> maps to
/// an empty value, <c>--key value</c> takes the next token when it is not itself an option, and
/// <c>--key=value</c> is accepted too. Tokens after <c>--</c> are never options.
/// </summary>
public sealed class CliArgs
{
    // Global switches that never take a value, so "wingman --fake check" keeps "check" as the command.
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "fake",
        "json",
        "help",
    };

    private readonly Dictionary<string, string> _options;

    private CliArgs(string? command, IReadOnlyList<string> positionals, Dictionary<string, string> options)
    {
        Command = command;
        Positionals = positionals;
        _options = options;
    }

    /// <summary>The first token that is not an option or an option's value, or null when there is none.</summary>
    public string? Command { get; }

    /// <summary>The tokens after <see cref="Command"/> that are not options or option values, in order.</summary>
    public IReadOnlyList<string> Positionals { get; }

    /// <summary>Option values by name without the leading dashes, compared case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Options => _options;

    public static CliArgs Parse(string[] args)
    {
        string? command = null;
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var optionsEnded = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            if (!optionsEnded && token == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (optionsEnded || !IsOption(token))
            {
                if (command is null)
                {
                    command = token;
                }
                else
                {
                    positionals.Add(token);
                }

                continue;
            }

            var (name, inlineValue) = SplitOption(token);
            if (inlineValue is not null)
            {
                options[name] = inlineValue;
                continue;
            }

            var nextIsValue = !BooleanFlags.Contains(name) && i + 1 < args.Length && !IsOption(args[i + 1]);
            if (nextIsValue)
            {
                options[name] = args[i + 1];
                i++;
            }
            else
            {
                options[name] = "";
            }
        }

        return new CliArgs(command, positionals, options);
    }

    public bool HasFlag(string name) => _options.ContainsKey(name);

    /// <summary>The option's value, <c>""</c> when it was given without one, or null when it was not given.</summary>
    public string? GetOption(string name) => _options.TryGetValue(name, out var value) ? value : null;

    // A lone "-" is a positional, conventionally standard input.
    private static bool IsOption(string token) => token.Length > 1 && token[0] == '-';

    private static (string Name, string? InlineValue) SplitOption(string token)
    {
        var isLong = token.StartsWith("--", StringComparison.Ordinal);
        var body = isLong ? token[2..] : token[1..];

        string name = body;
        string? inlineValue = null;
        var equals = body.IndexOf('=');
        if (equals >= 0)
        {
            name = body[..equals];
            inlineValue = body[(equals + 1)..];
        }

        var isShortHelp = !isLong && name == "h";
        return (isShortHelp ? "help" : name, inlineValue);
    }
}
