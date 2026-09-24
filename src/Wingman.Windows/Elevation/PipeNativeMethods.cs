using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wingman.Windows.Elevation;

/// <summary>The kernel32 calls that name the process at the other end of a connected pipe.</summary>
internal static class PipeNativeMethods
{
    // Enough to read an elevated process's image path from an unelevated one.
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // Longer than MAX_PATH, since an install under a deep folder can exceed it.
    private const int ImagePathCapacity = 32 * 1024;

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    /// <summary>
    /// The full Win32 path of <paramref name="processId"/>'s executable as the kernel recorded it,
    /// with links and short names already resolved; null when the process cannot be opened or read.
    /// </summary>
    public static string? GetProcessImagePath(uint processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = new char[ImagePathCapacity];
        var length = (uint)buffer.Length;
        if (!QueryFullProcessImageName(process, flags: 0, buffer, ref length))
        {
            return null;
        }

        return new string(buffer, 0, (int)length);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process, uint flags, [Out] char[] buffer, ref uint size);
}
