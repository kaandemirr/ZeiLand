using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand.Services;

/// <summary>
/// Registers the host window as an (invisible, zero-size) app bar so the shell tells us
/// when a full-screen app opens or closes — no foreground-window polling needed.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    private const int CallbackMessage = WM_APP + 2;

    private readonly HwndSource _host;
    private readonly HwndSourceHook _hook;
    private readonly int _taskbarCreated;

    public FullscreenWatcher(HwndSource host)
    {
        _host = host;
        _hook = WndProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        host.AddHook(_hook);
        Register();
    }

    public event Action<bool>? Changed;

    public bool IsFullscreen { get; private set; }

    public void Dispose()
    {
        var data = CreateData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _host.RemoveHook(_hook);
    }

    private void Register()
    {
        var data = CreateData();
        SHAppBarMessage(ABM_NEW, ref data);
    }

    private APPBARDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _host.Handle,
        uCallbackMessage = CallbackMessage,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage && (int)wParam == ABN_FULLSCREENAPP)
        {
            bool fullscreen = lParam != IntPtr.Zero && !IsDesktopForeground();
            if (fullscreen != IsFullscreen)
            {
                IsFullscreen = fullscreen;
                Changed?.Invoke(fullscreen);
            }
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            Register(); // Explorer restarted and forgot about us.
        }
        return IntPtr.Zero;
    }

    // Win+D / showing the desktop is reported as a "full-screen app" on some builds.
    private static bool IsDesktopForeground()
    {
        var className = new StringBuilder(32);
        GetClassName(GetForegroundWindow(), className, className.Capacity);
        return className.ToString() is "WorkerW" or "Progman";
    }
}
