using System.Globalization;

namespace Wingman.Core.Elevation;

/// <summary>
/// The command line of the elevated helper: <c>--elevated-worker &lt;pipe-name&gt; [--parent &lt;pid&gt;]</c>.
/// </summary>
public static class ElevatedWorkerArguments
{
    public const string Usage = "--elevated-worker <pipe-name> [--parent <pid>]";

    /// <summary>
    /// True when <paramref name="args"/> is exactly an elevated-worker command line. The parent
    /// process id is optional so a helper started by an older launcher still runs; it must be a
    /// positive decimal integer when present.
    /// </summary>
    /// <param name="parentPid">Null when <c>--parent</c> was not given.</param>
    public static bool TryParse(string[] args, out string pipe, out int? parentPid)
    {
        pipe = string.Empty;
        parentPid = null;

        switch (args)
        {
            case ["--elevated-worker", var name] when name.Length > 0:
                pipe = name;
                return true;

            case ["--elevated-worker", var name, "--parent", var pidText] when name.Length > 0:
                if (!TryParseProcessId(pidText, out var pid))
                {
                    return false;
                }

                pipe = name;
                parentPid = pid;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseProcessId(string text, out int pid) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
}
