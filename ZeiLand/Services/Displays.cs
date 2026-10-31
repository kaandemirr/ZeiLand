using System.Runtime.InteropServices;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand.Services;

/// <summary>A monitor, in physical pixels; <see cref="Device"/> (e.g. \\.\DISPLAY2) is what settings remember it by.</summary>
internal sealed record DisplayInfo(IntPtr Handle, string Device, RECT Bounds, RECT Work, bool IsPrimary)
{
    /// <summary>Windows' number for it, as in Settings → Display (\\.\DISPLAY2 → 2).</summary>
    public string Number => Device.TrimStart('\\', '.').Replace("DISPLAY", "", StringComparison.OrdinalIgnoreCase);
}

internal static class Displays
{
    public static List<DisplayInfo> GetAll()
    {
        var displays = new List<DisplayInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
                displays.Add(new DisplayInfo(monitor, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return displays;
    }

    /// <summary>The monitor with that device name; the primary one for null or one that's gone.</summary>
    public static DisplayInfo? Find(string? device)
    {
        var displays = GetAll();
        return displays.FirstOrDefault(d => device is not null && d.Device == device) ?? displays.FirstOrDefault(d => d.IsPrimary);
    }
}
