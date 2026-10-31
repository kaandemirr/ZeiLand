using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand.Services;

/// <summary>Notification-area icon (no WinForms dependency). Left click: settings; right click: the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = WM_APP + 1;

    private readonly HwndSource _host;
    private readonly HwndSourceHook _hook;
    private readonly int _taskbarCreated;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;

    public TrayIcon(HwndSource host)
    {
        _host = host;
        _hook = WndProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(host.Handle));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "island.ico");
        _icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        _ownsIcon = _icon != IntPtr.Zero;
        if (!_ownsIcon) _icon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);

        host.AddHook(_hook);
        Add();
    }

    public event Action? SettingsRequested;
    public event Action? MenuRequested;

    public void Dispose()
    {
        var data = CreateData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _host.RemoveHook(_hook);
        if (_ownsIcon) DestroyIcon(_icon);
    }

    private void Add()
    {
        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _host.Handle,
        uID = 1,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = "ZeiLand",
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int mouseMessage = (int)((long)lParam & 0xFFFF);
            if (mouseMessage == WM_LBUTTONUP) SettingsRequested?.Invoke();
            else if (mouseMessage == WM_RBUTTONUP) MenuRequested?.Invoke();
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted; the icon has to be added again.
        }
        return IntPtr.Zero;
    }
}
