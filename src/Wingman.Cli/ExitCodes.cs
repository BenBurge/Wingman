namespace Wingman.Cli;

/// <summary>The process exit codes every headless command returns.</summary>
public static class ExitCodes
{
    public const int Success = 0;

    public const int Failure = 1;

    /// <summary>The command line was wrong: an unknown command, a missing argument, or a bad option.</summary>
    public const int Usage = 2;

    /// <summary>Returned by <c>check</c> so a scheduled task or script can tell updates are waiting.</summary>
    public const int UpdatesAvailable = 10;

    /// <summary>128 + SIGINT, the code shells use for a Ctrl+C.</summary>
    public const int Canceled = 130;
}
