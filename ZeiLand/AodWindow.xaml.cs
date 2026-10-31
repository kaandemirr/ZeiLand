using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZeiLand.Services;
using ZeiLand.Settings;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

/// <summary>
/// The always-on display on one monitor: a black full screen with a dimmed clock, date, the song, a running
/// timer, the battery and the microphone / camera lights. Redrawn once a minute (once a second while a
/// timer counts), and when something it shows changes.
/// </summary>
public partial class AodWindow : Window
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const double MaxShift = 14;

    private readonly AppSettings _settings;
    private readonly MediaService _media;
    private readonly TimerService _timer;
    private readonly PrivacyMonitor _privacy;
    private readonly PowerService _power;
    private readonly DispatcherTimer _minute = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _second = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _hint = new() { Interval = TimeSpan.FromSeconds(5) };
    private DisplayInfo _display;

    internal AodWindow(AppSettings settings, MediaService media, TimerService timer, PrivacyMonitor privacy,
        PowerService power, DisplayInfo display)
    {
        InitializeComponent();
        _settings = settings;
        _media = media;
        _timer = timer;
        _privacy = privacy;
        _power = power;
        _display = display;

        _minute.Tick += (_, _) =>
        {
            Render();
            Move();
            ScheduleMinute();
        };
        _second.Tick += (_, _) => RenderTimer();
        _hint.Tick += (_, _) =>
        {
            _hint.Stop();
            HintText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(1)));
        };
        PlayPauseButton.Click += async (_, _) => await _media.TogglePlayPauseAsync();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) DoubleClicked?.Invoke();
        };

        SourceInitialized += (_, _) =>
        {
            // Never takes the focus from what the user is doing; not in Alt+Tab.
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
        Loaded += (_, _) =>
        {
            _media.Changed += Render;
            _timer.Changed += Render;
            _privacy.Changed += Render;
            _power.PowerSourceChanged += Render;
            _settings.PropertyChanged += OnSettingsChanged;
            Loc.Changed += Render;
            Render();
            ScheduleMinute();
        };
        Closed += (_, _) =>
        {
            _minute.Stop();
            _second.Stop();
            _hint.Stop();
            _media.Changed -= Render;
            _timer.Changed -= Render;
            _privacy.Changed -= Render;
            _power.PowerSourceChanged -= Render;
            _settings.PropertyChanged -= OnSettingsChanged;
            Loc.Changed -= Render;
        };
    }

    /// <summary>Double-clicked (turns it off when it's "always").</summary>
    public event Action? DoubleClicked;

    internal string Device => _display.Device;

    /// <summary>A short line at the bottom on how to close it; fades after a few seconds.</summary>
    internal void ShowHint(string text)
    {
        HintText.Text = text;
        HintText.BeginAnimation(OpacityProperty, null);
        HintText.Opacity = 1;
        _hint.Stop();
        _hint.Start();
    }

    /// <summary>Covers the whole monitor, in physical pixels (also after it moved or changed resolution).</summary>
    internal void Place(DisplayInfo display)
    {
        _display = display;
        Place();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var bounds = _display.Bounds;
        SetWindowPos(handle, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, SWP_NOACTIVATE);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("Aod", StringComparison.Ordinal) == true) Render();
    }

    private void ScheduleMinute()
    {
        var now = DateTime.Now;
        _minute.Stop();
        _minute.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _minute.Start();
    }

    /// <summary>A small random step, so no pixel stays lit in the same place for long.</summary>
    private void Move()
    {
        Shift.X = Random.Shared.NextDouble() * 2 * MaxShift - MaxShift;
        Shift.Y = Random.Shared.NextDouble() * 2 * MaxShift - MaxShift;
    }

    private void Render()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t", Loc.Culture);
        ClockText.Visibility = Shown(_settings.AodClock);
        DateText.Text = now.ToString(Loc.T("DateFormat"), Loc.Culture);
        DateText.Visibility = Shown(_settings.AodDate);

        bool media = _settings.AodMedia && _media.HasMedia;
        MediaRow.Visibility = Shown(media);
        if (media)
        {
            MediaTitle.Text = _media.Title;
            MediaArtist.Text = _media.Artist;
            MediaArt.Background = _media.Artwork is { } artwork
                ? new ImageBrush(artwork) { Stretch = Stretch.UniformToFill }.Frozen()
                : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)).Frozen();
            PlayPauseGlyph.Text = _media.IsPlaying ? PauseGlyph : PlayGlyph;
        }

        var power = PowerService.GetStatus();
        bool battery = _settings.AodBattery && power.HasBattery;
        BatteryInfo.Visibility = Shown(battery);
        if (battery)
        {
            BatteryGlyph.Level = power.Percent / 100.0;
            BatteryGlyph.IsCharging = power.OnAc;
            BatteryText.Text = Loc.Percent(power.Percent / 100.0);
        }

        bool microphone = _privacy.MicrophoneApps.Count > 0, camera = _privacy.CameraApps.Count > 0;
        PrivacyRow.Visibility = _settings.AodPrivacy && (microphone || camera) ? Visibility.Visible : Visibility.Hidden;
        MicDot.Visibility = Shown(microphone);
        CameraDot.Visibility = Shown(camera);

        RenderTimer();
    }

    /// <summary>The running timer, redrawn when its display changes (once a second, not while paused).</summary>
    private void RenderTimer()
    {
        _second.Stop();
        bool timer = _settings.AodTimer && _timer.IsActive;
        TimerInfo.Visibility = Shown(timer);
        InfoRow.Visibility = Shown(timer || BatteryInfo.Visibility == Visibility.Visible);
        if (!timer) return;

        TimerText.Text = TimerService.Format(_timer.Display);
        TimerText.Opacity = _timer.IsPaused ? 0.5 : 1;
        if (_timer.IsPaused) return;
        _second.Interval = _timer.UntilNextChange;
        _second.Start();
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
