using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Wingman.Core.Setup;

namespace Wingman.Windows.Setup;

/// <summary>The parts of a <c>.lnk</c> file <see cref="SetupExecutor"/> compares against a <see cref="ShortcutSpec"/>.</summary>
internal sealed record ShellLinkInfo(string TargetPath, string Arguments, string? AppUserModelId);

/// <summary>
/// Reads and writes <c>.lnk</c> files through the Shell's ShellLink COM object, including the
/// AppUserModelID property an unpackaged app needs on its Start Menu shortcut before Windows
/// shows its toasts.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellLink
{
    // IShellLinkW stores at most INFOTIPSIZE characters of arguments and MAX_PATH of path.
    private const int BufferLength = 1024;

    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31;

    private static readonly PropertyKey AppUserModelIdKey =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static void Save(string path, ShortcutSpec spec)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(spec.TargetPath);
            link.SetArguments(spec.Arguments);
            link.SetDescription(spec.Description);
            SetAppUserModelId((IPropertyStore)link, spec.AppUserModelId);
            ((IPersistFile)link).Save(path, fRemember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>The link at <paramref name="path"/>, or null when it is missing or unreadable.</summary>
    public static ShellLinkInfo? TryRead(string path)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, StgmRead);

            var target = new StringBuilder(BufferLength);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var arguments = new StringBuilder(BufferLength);
            link.GetArguments(arguments, arguments.Capacity);

            var appUserModelId = GetAppUserModelId((IPropertyStore)link);
            return new ShellLinkInfo(target.ToString(), arguments.ToString(), appUserModelId);
        }
        catch (Exception ex) when (IsShellLinkError(ex))
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// True for the exceptions a failing ShellLink call throws. The runtime maps well-known
    /// HRESULTs to .NET types, such as E_INVALIDARG for a malformed path to
    /// <see cref="ArgumentException"/>, and every other one to <see cref="COMException"/>.
    /// </summary>
    public static bool IsShellLinkError(Exception ex) =>
        ex is COMException or IOException or UnauthorizedAccessException or ArgumentException;

    private static void SetAppUserModelId(IPropertyStore store, string appUserModelId)
    {
        var key = AppUserModelIdKey;
        var value = new PropVariant { Type = VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(appUserModelId) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static string? GetAppUserModelId(IPropertyStore store)
    {
        var key = AppUserModelIdKey;
        store.GetValue(ref key, out var value);
        try
        {
            return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;

    // The vtable order must match shobjidl_core.h, so the unused methods stay.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant propvar);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    // PROPVARIANT is a 2-byte type tag, 6 reserved bytes, and a union two pointers wide; only the
    // first pointer is read here, but COM copies the whole union, so the struct must be full size.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr UnionTail;
    }
}
