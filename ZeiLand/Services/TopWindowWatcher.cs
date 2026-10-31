using System.Text;
using ZeiLand.Native;
using static ZeiLand.Native.NativeMethods;
using static ZeiLand.Native.WindowNative;

namespace ZeiLand.Services;

/// <summary>
/// Tells whether the active window reaches up under the compact island — a maximised browser's tab strip,
/// say — so the island can step aside. Driven by WinEvent hooks: foreground changes system-wide, and
/// moves/resizes within the foreground window's process only. Nothing is polled.
/// </summary>
internal sealed class TopWindowWatcher : IDisposable
{
    // Desktop, taskbars and similar shell surfaces never count as covering the island.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly WinEventProc _callback;
    private readonly IntPtr _foregroundHook;
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private IntPtr _locationHook;
    private uint _hookedProcess;
    private IntPtr _foreground;
    private RECT _zone;
    private bool _enabled;

    public TopWindowWatcher()
    {
        _callback = OnWinEvent; // Kept in a field so the delegate outlives the native hook.
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public event Action? Changed;

    /// <summary>True while the foreground window overlaps <see cref="Zone"/>.</summary>
    public bool IsCovered { get; private set; }

    /// <summary>Off: no location hook, and <see cref="IsCovered"/> stays false.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) OnForegroundChanged(GetForegroundWindow());
            else
            {
                Unhook();
                Set(false);
            }
        }
    }

    /// <summary>Screen area (physical pixels) the compact island occupies.</summary>
    public RECT Zone
    {
        get => _zone;
        set
        {
            _zone = value;
            Evaluate();
        }
    }

    public void Dispose()
    {
        Unhook();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!_enabled) return;
        if (eventType == EVENT_SYSTEM_FOREGROUND) OnForegroundChanged(hwnd);
        else if (hwnd == _foreground && idObject == OBJID_WINDOW && idChild == 0) Evaluate();
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        _foreground = hwnd;
        GetWindowThreadProcessId(hwnd, out uint process);
        if (process != _hookedProcess)
        {
            // Only this process's windows matter until the foreground changes again.
            Unhook();
            if (process != 0 && process != _ownProcess)
            {
                _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                    _callback, process, 0, WINEVENT_OUTOFCONTEXT);
                _hookedProcess = process;
            }
        }
        Evaluate();
    }

    private void Evaluate()
    {
        if (!_enabled) return;
        var hwnd = _foreground;
        bool covered = false;
        if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsShellWindow(hwnd)
            && DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var bounds,
                System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
        {
            covered = bounds.Left < _zone.Right && bounds.Right > _zone.Left
                && bounds.Top < _zone.Bottom && bounds.Bottom > _zone.Top;
        }
        Set(covered);
    }

    private bool IsShellWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint process);
        if (process == _ownProcess) return true;
        var name = new StringBuilder(64);
        return GetClassName(hwnd, name, name.Capacity) > 0 && ShellClasses.Contains(name.ToString());
    }

    private void Set(bool covered)
    {
        if (covered == IsCovered) return;
        IsCovered = covered;
        Changed?.Invoke();
    }

    private void Unhook()
    {
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        _locationHook = IntPtr.Zero;
        _hookedProcess = 0;
    }
}
