using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Wingman.Windows.Tray.NativeMethods;

namespace Wingman.Windows.Tray;

/// <summary>
/// The tray's badge icons, read from the <c>.ico</c> files embedded in this assembly so the
/// single-file exe needs nothing beside it. Each (file, size) pair is created once and destroyed
/// on <see cref="Dispose"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayIcons : IDisposable
{
    private const string ResourcePrefix = "Wingman.Windows.Tray.";
    private const int DirectoryHeaderSize = 6;
    private const int DirectoryEntrySize = 16;

    // The format version CreateIconFromResourceEx requires for icon resources.
    private const uint IconResourceVersion = 0x00030000;

    private readonly Dictionary<(string FileName, int Size), nint> _icons = [];

    /// <summary>
    /// Returns the HICON for the frame of <paramref name="fileName"/> that best fits
    /// <paramref name="size"/> pixels, or 0 when the resource is missing or unreadable.
    /// </summary>
    public nint Load(string fileName, int size)
    {
        if (_icons.TryGetValue((fileName, size), out var cached))
        {
            return cached;
        }

        var bytes = ReadResource(fileName);
        if (bytes is null)
        {
            TrayWindow.Log($"embedded resource {ResourcePrefix}{fileName} is missing");
            return 0;
        }

        if (!TryFindFrame(bytes, size, out var offset, out var length))
        {
            TrayWindow.Log($"embedded resource {ResourcePrefix}{fileName} is not a valid .ico");
            return 0;
        }

        // Accepts both BMP and PNG-compressed frames; Windows has read PNG frames since Vista.
        var frame = bytes.AsSpan(offset, length).ToArray();
        var icon = CreateIconFromResourceEx(frame, (uint)length, true, IconResourceVersion, size, size, LR_DEFAULTCOLOR);
        if (icon == 0)
        {
            TrayWindow.Log($"CreateIconFromResourceEx failed for {fileName} (error {Marshal.GetLastPInvokeError()})");
            return 0;
        }

        _icons[(fileName, size)] = icon;
        TrayWindow.Log($"loaded embedded {fileName} at {size}x{size}: HICON 0x{icon:X}");
        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon);
        }

        _icons.Clear();
    }

    private static byte[]? ReadResource(string fileName)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Picks the frame whose width equals <paramref name="size"/>, else the smallest larger one,
    /// else the largest, and returns where its image bytes sit in the file.
    /// </summary>
    private static bool TryFindFrame(byte[] bytes, int size, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        if (bytes.Length < DirectoryHeaderSize)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        if (type != 1 || count == 0 || bytes.Length < DirectoryHeaderSize + (count * DirectoryEntrySize))
        {
            return false;
        }

        var exact = -1;
        var smallestLarger = -1;
        var largest = -1;
        var smallestLargerWidth = int.MaxValue;
        var largestWidth = 0;
        for (var index = 0; index < count; index++)
        {
            var entryStart = DirectoryHeaderSize + (index * DirectoryEntrySize);

            // A stored width of 0 means 256, the one size a byte cannot hold.
            var width = bytes[entryStart] == 0 ? 256 : bytes[entryStart];
            if (width == size && exact < 0)
            {
                exact = index;
            }

            if (width > size && width < smallestLargerWidth)
            {
                smallestLarger = index;
                smallestLargerWidth = width;
            }

            if (width > largestWidth)
            {
                largest = index;
                largestWidth = width;
            }
        }

        int chosen;
        if (exact >= 0)
        {
            chosen = exact;
        }
        else if (smallestLarger >= 0)
        {
            chosen = smallestLarger;
        }
        else
        {
            chosen = largest;
        }

        var chosenStart = DirectoryHeaderSize + (chosen * DirectoryEntrySize);
        var frameLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chosenStart + 8));
        var frameOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(chosenStart + 12));
        var fits = frameLength > 0 && frameOffset + (ulong)frameLength <= (ulong)bytes.Length;
        if (!fits)
        {
            return false;
        }

        offset = (int)frameOffset;
        length = (int)frameLength;
        return true;
    }
}
