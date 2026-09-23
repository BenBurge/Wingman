namespace Wingman.Core.Setup;

/// <summary>
/// Full paths to the Windows programs Wingman starts, so a same-named program earlier on PATH
/// (GNU <c>timeout</c> under Git Bash, for one) never runs in their place.
/// </summary>
internal static class SystemTool
{
    /// <summary>
    /// <paramref name="fileName"/> in <paramref name="systemDirectory"/>, joined with a backslash
    /// whatever the host OS, since the path is only ever used on Windows. An empty directory, as
    /// <see cref="Environment.SystemDirectory"/> returns off Windows, leaves the bare name.
    /// </summary>
    public static string PathIn(string systemDirectory, string fileName)
    {
        if (systemDirectory.Length == 0)
        {
            return fileName;
        }

        return systemDirectory.TrimEnd('\\') + "\\" + fileName;
    }
}
