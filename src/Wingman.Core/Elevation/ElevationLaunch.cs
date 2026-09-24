using System.Text;
using Wingman.Core.Settings;
using Wingman.Core.Setup;

namespace Wingman.Core.Elevation;

/// <summary>
/// The program and argument string an elevation prompt starts, for each <see cref="ElevationLauncher"/>.
/// Nothing here starts a process.
/// </summary>
public static class ElevationLaunch
{
    /// <summary>
    /// <paramref name="exePath"/> with <paramref name="args"/> for <see cref="ElevationLauncher.Direct"/>;
    /// for <see cref="ElevationLauncher.PowerShell"/>, Windows PowerShell 5.1 from
    /// <paramref name="systemDirectory"/> running <c>&amp; '&lt;exe&gt;' '&lt;arg&gt;' …</c>, which
    /// waits for Wingman to exit, so the wrapper lives exactly as long as the process it started.
    /// </summary>
    /// <param name="hidden">Adds <c>-WindowStyle Hidden</c>, for the helper; the caller also sets
    /// the matching window style when it starts the process.</param>
    /// <returns>A file name and one argument string for <c>ProcessStartInfo.Arguments</c>, since
    /// <c>ArgumentList</c> cannot express the PowerShell command's own quoting.</returns>
    public static (string FileName, string Arguments) Build(
        ElevationLauncher launcher, string systemDirectory, string exePath, IReadOnlyList<string> args, bool hidden)
    {
        if (launcher == ElevationLauncher.Direct)
        {
            return (exePath, JoinCommandLine(args));
        }

        var command = new StringBuilder("& ").Append(PowerShellLiteral(exePath));
        foreach (var arg in args)
        {
            command.Append(' ').Append(PowerShellLiteral(arg));
        }

        var arguments = new StringBuilder("-NoProfile -ExecutionPolicy Bypass ");
        if (hidden)
        {
            arguments.Append("-WindowStyle Hidden ");
        }

        arguments.Append("-Command ").Append(QuoteCommandLineArgument(command.ToString()));

        var powerShell = SystemTool.PathIn(systemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return (powerShell, arguments.ToString());
    }

    /// <summary>A PowerShell single-quoted string, in which only a doubled <c>'</c> is special.</summary>
    private static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string JoinCommandLine(IReadOnlyList<string> args)
    {
        var quoted = new List<string>(args.Count);
        foreach (var arg in args)
        {
            quoted.Add(QuoteCommandLineArgument(arg));
        }

        return string.Join(' ', quoted);
    }

    /// <summary>
    /// Quotes <paramref name="arg"/> the way <c>CommandLineToArgvW</c> and the C runtime split it
    /// back: backslashes are literal except before a <c>"</c>, where they and the quote are escaped.
    /// </summary>
    private static string QuoteCommandLineArgument(string arg)
    {
        var needsQuotes = arg.Length == 0 || arg.AsSpan().IndexOfAny(" \t\"") >= 0;
        if (!needsQuotes)
        {
            return arg;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                quoted.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                quoted.Append('\\', backslashes);
            }

            backslashes = 0;
            quoted.Append(c);
        }

        quoted.Append('\\', backslashes * 2);
        return quoted.Append('"').ToString();
    }
}
