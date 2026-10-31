using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static ZeiLand.Native.WindowNative;

namespace ZeiLand.Services;

/// <summary>
/// App icons as the shell draws them (the same images as Start and the taskbar), cached per app and size.
/// Only called on the UI thread, and only when the media source or the optimize list changes.
/// </summary>
internal static class AppIcons
{
    private const int IconOnly = 0x4; // SIIGBF_ICONONLY: never a thumbnail of the file's contents

    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Icon for a media session's source app: an AppUserModelID such as "MSEdge" or
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", or an executable name such as "Spotify.exe".
    /// </summary>
    public static BitmapSource? ForAppId(string appId, int size) => Cached($"id:{appId}:{size}", () =>
        FromShell(@"shell:AppsFolder\" + appId, size)
        ?? (RunningPath(Path.GetFileNameWithoutExtension(appId)) is { } path ? FromShell(path, size) : null));

    public static BitmapSource? ForPath(string path, int size) => Cached($"path:{path}:{size}", () => FromShell(path, size));

    private static BitmapSource? Cached(string key, Func<BitmapSource?> load)
    {
        if (!Cache.TryGetValue(key, out var icon)) Cache[key] = icon = load();
        return icon;
    }

    private static string? RunningPath(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Select(p => GetProcessPath(p.Id)).FirstOrDefault(p => p is not null);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static BitmapSource? FromShell(string parsingName, int size)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory);
            factory.GetImage(new SIZE { cx = size, cy = size }, IconOnly, out bitmap);
            Marshal.ReleaseComObject(factory);
            return ToBitmapSource(bitmap);
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // Not an app the shell knows.
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    // Imaging.CreateBitmapSourceFromHBitmap drops the alpha channel, so read the 32-bit pixels directly.
    private static BitmapSource? ToBitmapSource(IntPtr bitmap)
    {
        var info = new BITMAP();
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0 || info.bmBitsPixel != 32 || info.bmBits == IntPtr.Zero)
            return null;

        int width = info.bmWidth, height = Math.Abs(info.bmHeight), stride = info.bmWidthBytes;
        var pixels = new byte[stride * height];
        Marshal.Copy(info.bmBits, pixels, 0, pixels.Length);
        if (info.bmHeight > 0) FlipRows(pixels, stride, height); // Bottom-up DIB.

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static void FlipRows(byte[] pixels, int stride, int height)
    {
        var row = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr gdiObject, int size, ref BITMAP bitmap);
}
