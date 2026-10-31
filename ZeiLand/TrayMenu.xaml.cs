using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

/// <summary>
/// The tray / right-click menu, styled like the settings window. Opens at the pointer, closes when it
/// loses focus, on Escape, or after a choice.
/// </summary>
public partial class TrayMenu : Window
{
    private bool _closing;

    public TrayMenu(bool autostart, bool aod)
    {
        InitializeComponent();
        VersionText.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        AutostartItem.IsChecked = autostart;
        AodItem.IsChecked = aod;
        AodItem.Click += (_, _) =>
        {
            bool enabled = AodItem.IsChecked == true;
            Close();
            AodToggled?.Invoke(enabled);
        };

        SettingsItem.Click += (_, _) => Choose(SettingsClicked);
        ExitItem.Click += (_, _) => Choose(ExitClicked);
        AutostartItem.Click += (_, _) =>
        {
            bool enabled = AutostartItem.IsChecked == true;
            Close(); // A UAC prompt may follow; the menu shouldn't wait behind it.
            AutostartToggled?.Invoke(enabled);
        };

        // Closing deactivates the menu too, which mustn't close it a second time.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (!_closing) Close();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        };
    }

    public event Action? SettingsClicked;
    public event Action<bool>? AutostartToggled;
    public event Action<bool>? AodToggled;
    public event Action? ExitClicked;

    /// <summary>
    /// Shows the menu with its corner at the pointer, opening upwards and leftwards when there's no room
    /// (e.g. the tray in the bottom-right corner).
    /// </summary>
    public void ShowAtPointer()
    {
        GetCursorPos(out var pointer);
        Show(); // Laid out (and still invisible) so its size is known.

        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromPoint(pointer, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info) && GetWindowRect(handle, out var rect))
        {
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            // The window includes a transparent margin for the shadow; line up the visible panel instead.
            int inset = (int)Math.Round(Panel.Margin.Left * VisualTreeHelperDpi());
            var work = info.rcWork;
            int x = pointer.X - inset;
            int y = pointer.Y - inset;
            if (x + width > work.Right) x = pointer.X - width + inset;
            if (y + height > work.Bottom) y = pointer.Y - height + inset;
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
            SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE);
        }

        // Allowed because our process just received the click that opened the menu.
        SetForegroundWindow(handle);
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    private double VisualTreeHelperDpi() => System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void Choose(Action? action)
    {
        Close();
        action?.Invoke();
    }
}
