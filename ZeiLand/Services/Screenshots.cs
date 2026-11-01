using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeiLand.Native;

namespace ZeiLand.Services;

/// <summary>
/// Screenshots taken with Win+Shift+S or the Snipping Tool: told apart from other images by the tool that put
/// them on the clipboard. The latest one is kept as a file, so it can be opened or saved after the clipboard
/// has moved on.
/// </summary>
internal static class Screenshots
{
    private static readonly HashSet<string> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        "SnippingTool", "ScreenClippingHost", "ScreenSketch",
    };

    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "ZeiLand");
    private const string TempPrefix = "screenshot-";

    // FOLDERID_Screenshots: Pictures\Screenshots, wherever the user has moved it.
    private static readonly Guid ScreenshotsFolder = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    public static bool IsFromScreenshotTool(IntPtr clipboardOwner) =>
        WindowNative.GetWindowProcessName(clipboardOwner) is { } name && Tools.Contains(name);

    /// <summary>The image on the clipboard, frozen; null if there's none or another app has the clipboard open.</summary>
    public static BitmapSource? ReadClipboard()
    {
        try
        {
            // The snipping tool also puts a PNG there. The plain bitmap's unused alpha byte would read as see-through.
            BitmapSource? image = Clipboard.GetData("PNG") is MemoryStream png
                ? BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                : Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap
                    ? new WriteableBitmap(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0))
                    : null;
            image?.Freeze();
            return image;
        }
        catch (Exception e) when (e is ExternalException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>Writes the screenshot to a temporary file (replacing the previous one); null if that failed.</summary>
    public static string? SaveTemporary(BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(TempFolder);
            foreach (var old in Directory.EnumerateFiles(TempFolder, TempPrefix + "*.png")) File.Delete(old);
            var path = Path.Combine(TempFolder, $"{TempPrefix}{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(path)) encoder.Save(file);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Copies it into the Screenshots folder under a dated name; the new file's path, or null.</summary>
    public static string? SaveToScreenshots(string temporary, string name)
    {
        try
        {
            string folder = KnownFolder(ScreenshotsFolder)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(folder);
            string stem = $"{name} {DateTime.Now:yyyy-MM-dd HHmmss}", path = Path.Combine(folder, stem + ".png");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem} ({i}).png");
            File.Copy(temporary, path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Opens it in the default image viewer, as the user (see <see cref="Shortcuts.Launch"/>).</summary>
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false })?.Dispose();

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
