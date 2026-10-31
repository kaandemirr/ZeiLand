using System.Windows.Input;
using System.Windows.Interop;
using static ZeiLand.Native.WindowNative;

namespace ZeiLand.Services;

/// <summary>System-wide keyboard shortcuts, registered on the hidden host window (no keyboard hook).</summary>
internal sealed class Hotkeys : IDisposable
{
    private const int MicrophoneId = 1;

    private readonly HwndSource _host;
    private readonly HwndSourceHook _hook;

    public Hotkeys(HwndSource host)
    {
        _host = host;
        _hook = WndProc;
        host.AddHook(_hook);
    }

    private string? _microphone;
    private bool _suspended;

    public event Action? MicrophonePressed;

    /// <summary>False when the shortcut is set but couldn't be registered (another app owns it).</summary>
    public bool MicrophoneRegistered { get; private set; } = true;

    /// <summary>Registers the microphone shortcut, e.g. "Ctrl+Alt+K"; empty removes it.</summary>
    public void SetMicrophone(string? gesture)
    {
        _microphone = gesture;
        Register();
    }

    /// <summary>
    /// Turns the shortcuts off while the settings window records a new one; a registered shortcut would
    /// swallow the very key press being recorded.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            Register();
        }
    }

    private void Register()
    {
        UnregisterHotKey(_host.Handle, MicrophoneId);
        if (_suspended) return;
        MicrophoneRegistered = string.IsNullOrWhiteSpace(_microphone)
            || (TryParse(_microphone, out var modifiers, out var key)
                && RegisterHotKey(_host.Handle, MicrophoneId, ToNative(modifiers) | MOD_NOREPEAT,
                    (uint)KeyInterop.VirtualKeyFromKey(key)));
    }

    public void Dispose()
    {
        UnregisterHotKey(_host.Handle, MicrophoneId);
        _host.RemoveHook(_hook);
    }

    /// <summary>"Ctrl+Alt+K" from a key press; null for a key that can't be a shortcut on its own.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        // A bare letter would fire while typing; function keys are fine alone.
        if (modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24)) return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string gesture, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    private static uint ToNative(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0)
        | (modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == MicrophoneId)
        {
            MicrophonePressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
