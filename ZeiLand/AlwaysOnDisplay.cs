using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using ZeiLand.Native;
using ZeiLand.Services;
using ZeiLand.Settings;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

/// <summary>
/// Decides when and where the always-on display shows. "Always": on the chosen screens for as long as it's
/// turned on (a double-click turns it off), but not over a full-screen app. "When idle": once nobody has
/// touched the mouse, keyboard or a game controller for a few minutes, until they do; over a full-screen game
/// left alone too, but not over a video being watched. While it shows, the screen stays on.
/// </summary>
internal sealed class AlwaysOnDisplay : IDisposable
{
    private static readonly TimeSpan InputCheck = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryWhenCovered = TimeSpan.FromMinutes(1);

    // Players that don't tell Windows what they play: full screen, they're showing a film.
    private static readonly HashSet<string> VideoPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "PotPlayer", "PotPlayer64", "PotPlayerMini", "PotPlayerMini64",
        "mpv", "KMPlayer", "KMPlayer64X", "GOM", "smplayer", "wmplayer",
    };

    private readonly AppSettings _settings;
    private readonly MediaService _media;
    private readonly TimerService _timer;
    private readonly PrivacyMonitor _privacy;
    private readonly PowerService _power;
    private readonly FullscreenWatcher _fullscreen;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _idleTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _inputTimer = new(DispatcherPriority.Background) { Interval = InputCheck };
    private readonly List<AodWindow> _windows = [];
    private readonly Gamepads _gamepads = new();
    private bool _dismissOnInput;
    private uint _inputWhenShown;

    public AlwaysOnDisplay(AppSettings settings, MediaService media, TimerService timer, PrivacyMonitor privacy,
        PowerService power, FullscreenWatcher fullscreen, Dispatcher dispatcher)
    {
        _settings = settings;
        _media = media;
        _timer = timer;
        _privacy = privacy;
        _power = power;
        _fullscreen = fullscreen;
        _dispatcher = dispatcher;

        // Waiting for idle costs one timer tick per few minutes of activity; only a showing AOD watches input closely.
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            Update();
        };
        _inputTimer.Tick += (_, _) =>
        {
            if (LastInput() != _inputWhenShown || _gamepads.Sample()) Hide();
        };
        _settings.PropertyChanged += OnSettingsChanged;
        _fullscreen.Changed += OnFullscreenChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }

    public bool IsShowing => _windows.Count > 0;

    public void Start() => Update();

    /// <summary>Shows it now, until the next mouse move or key press (from the settings, to try it).</summary>
    public void Preview() => Show(dismissOnInput: true);

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _fullscreen.Changed -= OnFullscreenChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _idleTimer.Stop();
        _gamepads.Stop();
        CloseAll();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.AodEnabled) or nameof(AppSettings.AodTrigger) or nameof(AppSettings.AodIdleMinutes)
            or nameof(AppSettings.AodScreen) or nameof(AppSettings.IslandMonitor))
            Update();
    }

    private void OnFullscreenChanged(bool fullscreen) => Update();

    private void OnDisplaysChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(() =>
    {
        foreach (var window in _windows) // a resolution change: cover the new size
            if (Displays.GetAll().FirstOrDefault(d => d.Device == window.Device) is { } display) window.Place(display);
        Update();
    });

    private void Update()
    {
        if (!_settings.AodEnabled)
        {
            _idleTimer.Stop();
            _gamepads.Stop();
            if (IsShowing && !_dismissOnInput) Hide(); // a preview stays until the next input
            return;
        }
        if (_settings.AodTrigger == AodTrigger.Always)
        {
            _gamepads.Stop();
            Show(dismissOnInput: false);
            return;
        }

        if (IsShowing && !_dismissOnInput) Hide(); // switched from "always" to "when idle"
        if (IsShowing)
        {
            Show(dismissOnInput: true); // screens or full-screen apps changed
            return;
        }

        _gamepads.Watch();
        var wait = TimeSpan.FromMinutes(_settings.AodIdleMinutes) - IdleTime();
        if (wait <= TimeSpan.Zero && Show(dismissOnInput: true)) return;
        _idleTimer.Stop();
        _idleTimer.Interval = wait > TimeSpan.Zero ? wait + TimeSpan.FromMilliseconds(200) : RetryWhenCovered;
        _idleTimer.Start();
    }

    /// <summary>Brings up (or keeps) a window on each screen it should be on. False if there's none to show.</summary>
    private bool Show(bool dismissOnInput)
    {
        _idleTimer.Stop();
        _dismissOnInput = dismissOnInput;
        var targets = Targets();

        // Windows on screens it shouldn't be on any more go; new screens get one.
        foreach (var window in _windows.Where(w => targets.All(t => t.Device != w.Device)).ToList())
        {
            _windows.Remove(window);
            window.Close();
        }
        foreach (var display in targets.Where(t => _windows.All(w => w.Device != t.Device)))
        {
            var window = new AodWindow(_settings, _media, _timer, _privacy, _power, display);
            window.DoubleClicked += OnDoubleClicked;
            window.Show();
            _windows.Add(window);
        }
        if (_windows.Count == 0)
        {
            CloseAll();
            return false;
        }

        string hint = Loc.T(dismissOnInput ? "AodHintIdle" : "AodHintAlways");
        foreach (var window in _windows) window.ShowHint(hint);
        SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED);
        if (dismissOnInput)
        {
            _inputWhenShown = LastInput();
            _gamepads.Sample();
            _inputTimer.Start();
        }
        else
        {
            _inputTimer.Stop();
        }
        return true;
    }

    /// <summary>Closes it; when it's turned on, it then waits for the next idle stretch (or, "always", comes right back).</summary>
    private void Hide()
    {
        CloseAll();
        if (_settings.AodEnabled) Update();
    }

    private void CloseAll()
    {
        _inputTimer.Stop();
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        SetThreadExecutionState(ES_CONTINUOUS); // the screen may turn off again
    }

    private void OnDoubleClicked()
    {
        if (_dismissOnInput) Hide();
        else _settings.AodEnabled = false;
    }

    /// <summary>The chosen screens, minus one with a full-screen app it shouldn't cover.</summary>
    private List<DisplayInfo> Targets()
    {
        var displays = Displays.GetAll();
        var chosen = _settings.AodScreen switch
        {
            "*" => displays,
            { } device when displays.Any(d => d.Device == device) => displays.Where(d => d.Device == device).ToList(),
            _ => displays.Where(d => d.Device == (Displays.Find(_settings.IslandMonitor)?.Device)).ToList(),
        };
        // Waiting for idle, a full-screen game left alone gets covered too, a video being watched doesn't, nor a
        // game holding the screen exclusively (nothing shows over it, and trying may knock it out of full screen).
        // "Always" stays off that screen: someone is playing or watching there.
        var (window, covered) = FullscreenApp();
        if (covered == IntPtr.Zero
            || (_settings.AodTrigger == AodTrigger.Idle && !IsPlayingVideo(window) && !IsExclusiveFullscreen()))
            return chosen;
        return chosen.Where(d => d.Handle != covered).ToList();
    }

    private static bool IsExclusiveFullscreen() =>
        SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN;

    /// <summary>
    /// Whether a full-screen app is showing a video: it's playing media itself (a browser, Films &amp; TV),
    /// matched by the app id it shows on the taskbar or its executable's name, or it's a known video player.
    /// </summary>
    private bool IsPlayingVideo(IntPtr window)
    {
        WindowNative.GetWindowThreadProcessId(window, out uint process);
        string? name = WindowNative.GetProcessPath((int)process) is { } path ? Path.GetFileNameWithoutExtension(path) : null;
        if (name is not null && VideoPlayers.Contains(name)) return true;

        string? windowApp = WindowNative.GetWindowAppId(window);
        return _media.GetPlayingApps().Any(app => app.Equals(windowApp, StringComparison.OrdinalIgnoreCase)
            || (name is not null && (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app)
                .Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The full-screen app the shell reported and its monitor, if the foreground window really is one: it fills
    /// the monitor and has no title bar (unlike a maximised window under an auto-hiding taskbar). Our own
    /// windows don't count; the shell may well take the AOD itself for a full-screen app.
    /// </summary>
    private (IntPtr Window, IntPtr Monitor) FullscreenApp()
    {
        if (!_fullscreen.IsFullscreen) return default;
        var foreground = GetForegroundWindow();
        WindowNative.GetWindowThreadProcessId(foreground, out uint process);
        if (process == Environment.ProcessId || (GetWindowLong(foreground, GWL_STYLE) & WS_CAPTION) == WS_CAPTION
            || !GetWindowRect(foreground, out var rect)) return default;

        var monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        var display = Displays.GetAll().FirstOrDefault(d => d.Handle == monitor);
        if (display is null) return default;
        var bounds = display.Bounds;
        bool fills = rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
        return fills ? (foreground, monitor) : default;
    }

    private static uint LastInput()
    {
        var info = new LASTINPUTINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    /// <summary>Since the last mouse, keyboard or game-controller input.</summary>
    private TimeSpan IdleTime()
    {
        var idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - LastInput()));
        return _gamepads.LastUsed is { } used && Environment.TickCount64 - used < idle.TotalMilliseconds
            ? TimeSpan.FromMilliseconds(Environment.TickCount64 - used) : idle;
    }
}
