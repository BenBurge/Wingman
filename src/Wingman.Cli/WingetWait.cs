namespace Wingman.Cli;

/// <summary>
/// Prints <c>checking winget…</c> to stderr while a command's first winget call is in flight, so a
/// person watching an interactive terminal knows the command has not hung. Silent under
/// <c>--json</c>, whose stdout must stay a single parseable document, and when output is
/// redirected, since no one is watching a line meant to be overwritten.
/// </summary>
internal static class WingetWait
{
    private const string Notice = "checking winget…";

    public static IDisposable Begin(CliContext context)
    {
        if (context.IsOutputRedirected || context.Json)
        {
            return NullScope.Instance;
        }

        context.Error.Write(Notice);
        return new ClearingScope(context.Error);
    }

    private sealed class ClearingScope(TextWriter error) : IDisposable
    {
        public void Dispose() => error.Write($"\r{new string(' ', Notice.Length)}\r");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
