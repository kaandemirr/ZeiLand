using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ZeiLand.Services;
using ZeiLand.Settings;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

// Where the island sits: on the top edge of a monitor's work area, in the middle unless moved. Ctrl + drag
// moves it along that edge, onto another monitor too; it's remembered as the monitor plus a fraction of
// its width, so it stays put when the resolution or scaling changes.
public partial class IslandWindow
{
    // Within this distance of the middle, a drag snaps to it, so the default spot is easy to find again.
    private const double SnapToMiddle = 24;

    private bool _dragging;
    private int _dragOffset; // pointer minus the island's centre when the drag started, physical pixels

    /// <summary>Back above the other always-on-top windows, e.g. a full-screen game that keeps itself on top too.</summary>
    public void BringToTop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void InitializePlacement()
    {
        Root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true; // neither opens the island nor presses a button under the pointer
            StartDrag();
        };
        Root.PreviewMouseMove += (_, _) =>
        {
            if (_dragging) Drag();
        };
        Root.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            e.Handled = true;
            EndDrag();
        };
        Root.LostMouseCapture += (_, _) =>
        {
            if (_dragging) EndDrag();
        };
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.IslandMonitor) or nameof(AppSettings.IslandPosition)) PositionIsland();
        };
    }

    private void StartDrag()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetCursorPos(out var cursor) || !GetWindowRect(handle, out var rect)) return;
        _dragging = true;
        _dragOffset = cursor.X - (rect.Left + rect.Right) / 2;
        // The compact island is what moves; it opens again on the next hover.
        _hoverTimer.Stop();
        _leaveTimer.Stop();
        SetExpanded(false);
        Root.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeWE;
    }

    private void Drag()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetCursorPos(out var cursor) || !GetWindowRect(handle, out var rect)) return;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref info)) return;

        int centre = cursor.X - _dragOffset;
        int middle = (info.rcMonitor.Left + info.rcMonitor.Right) / 2;
        if (Math.Abs(centre - middle) <= SnapToMiddle * VisualTreeHelper.GetDpi(this).DpiScaleX) centre = middle;
        Place(handle, Clamp(centre, rect.Right - rect.Left, info.rcMonitor), info.rcWork.Top, rect.Right - rect.Left);
    }

    private void EndDrag()
    {
        _dragging = false;
        Mouse.OverrideCursor = null;
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();

        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var rect)) return;
        var centre = new POINT { X = (rect.Left + rect.Right) / 2, Y = rect.Top };
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST), ref info)) return;

        double fraction = (double)(centre.X - info.rcMonitor.Left) / (info.rcMonitor.Right - info.rcMonitor.Left);
        _settings.IslandMonitor = (info.dwFlags & MONITORINFOF_PRIMARY) != 0 ? null : info.szDevice;
        _settings.IslandPosition = Math.Abs(fraction - 0.5) < 0.002 ? 0.5 : Math.Round(fraction, 4);
        PositionIsland(); // in case neither changed: still settles the top-window zone
    }

    /// <summary>Puts the island where the settings say, in physical pixels; the primary monitor if that one's gone.</summary>
    private void PositionIsland()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || _dragging) return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(FindMonitor(_settings.IslandMonitor), ref info) || !GetWindowRect(handle, out var rect)) return;

        int width = rect.Right - rect.Left;
        int centre = info.rcMonitor.Left + (int)Math.Round(_settings.IslandPosition * (info.rcMonitor.Right - info.rcMonitor.Left));
        Place(handle, Clamp(centre, width, info.rcMonitor), info.rcWork.Top, width);
    }

    private void Place(IntPtr handle, int centre, int top, int width)
    {
        SetWindowPos(handle, HWND_TOPMOST, centre - width / 2, top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

        // Where the compact views sit on screen: windows reaching into this area make them step aside.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int halfWidth = (int)Math.Ceiling(CompactMediaView.Width / 2 * scale);
        _topWindows.Zone = new RECT
        {
            Left = centre - halfWidth,
            Right = centre + halfWidth,
            Top = top,
            Bottom = top + (int)Math.Ceiling((CompactMediaView.Margin.Top + CompactMediaView.Height) * scale),
        };
    }

    /// <summary>Keeps the whole (invisible) window on the monitor, so it never straddles two with different scaling.</summary>
    private static int Clamp(int centre, int width, RECT monitor)
    {
        int min = monitor.Left + width / 2, max = monitor.Right - width / 2;
        return min <= max ? Math.Clamp(centre, min, max) : (monitor.Left + monitor.Right) / 2;
    }

    private static IntPtr FindMonitor(string? device) =>
        Displays.Find(device)?.Handle ?? MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
}
