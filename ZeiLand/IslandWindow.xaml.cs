using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZeiLand.Animation;
using ZeiLand.Native;
using ZeiLand.Services;
using ZeiLand.Settings;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

public partial class IslandWindow : Window
{
    private enum View
    {
        Idle, CompactMedia, CompactTimer, CompactCall, ExpandedMedia, Home,
        Optimize, Devices, TimerPicker, // panels opened from the expanded island
        Volume, Power, Microphone, Output, Notice, // transient
        Screenshot, Notification, // transient, and they stay while the pointer is on them
        TimerDone,
    }

    private const double MaxRadius = 32;

    // The window's size at 100%; the island size setting scales the window and everything in it.
    private const double BaseWidth = 472, BaseHeight = 320;

    // Apps whose use of the microphone means a call (executable names).
    private static readonly HashSet<string> CallApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord", "DiscordPTB", "DiscordCanary", "ms-teams", "Teams", "Zoom", "Slack", "Skype",
        "WhatsApp", "WhatsApp.Root", "Telegram", "Signal", "Viber", "CiscoCollabHost", "Webex",
    };

    /// <summary>The room for the user's GIF, left of the player's controls.</summary>
    internal static readonly Size GifSlot = new(84, 38);
    internal const double GifSlotRadius = 6;

    private static readonly Geometry PlayGlyph = Geometry.Parse("M1,0 L15,9 L1,18 Z").Frozen();
    private static readonly Geometry PauseGlyph = Geometry.Parse("M1,0 H5.5 V18 H1 Z M9.5,0 H14 V18 H9.5 Z").Frozen();
    private static readonly Brush ArtPlaceholder = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)).Frozen();
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)).Frozen();
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)).Frozen();
    private static readonly Brush DangerButtonBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Brush SecondaryBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush SelectedTabBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush TertiaryBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)).Frozen();

    private const int OptimizeRows = 6;
    private const int DeviceRows = 5;

    private const string MicrophoneGlyphText = "";
    private const string MicrophoneOffGlyphText = "";
    private const string HeadphonesGlyphText = "";
    private const string SpeakerGlyphText = "";
    private const string CheckGlyphText = "";
    private static readonly int[] TimerPresetMinutes = [1, 3, 5, 10, 15, 25, 45, 60];
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut }.Frozen();

    // Level bars' resting heights (paused, or animation turned off); playing, they follow the audio.
    private static readonly double[] LevelRest = [0.55, 1.0, 0.7, 0.35];

    private readonly MediaService _media;
    private readonly VolumeService _volume;
    private readonly SystemStatsService _stats;
    private readonly ProcessMonitor _processes;
    private readonly AppSettings _settings;
    private readonly PrivacyMonitor _privacy;
    private readonly MicrophoneService _microphone;
    private readonly TimerService _timer;
    private readonly LevelMeter _levelMeter;
    private readonly TopWindowWatcher _topWindows = new();
    private readonly NotificationWatcher _notifications = new();
    private readonly Dictionary<View, FrameworkElement> _views;
    private readonly RectangleGeometry _shape = new();
    private readonly RectangleGeometry _bridge = new(Rect.Empty);
    private Rect _shapeTarget = Rect.Empty; // where the island is morphing to
    private Rect _skirt = Rect.Empty; // area the open island shrank away from under the pointer
    private readonly SpringAnimator _shapeAnimator;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _transientTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _optimizeTimer;
    private readonly DispatcherTimer _disarmTimer;
    private readonly DispatcherTimer _timerTick;
    private readonly DispatcherTimer _callTick;

    private View _current = View.Idle;
    private View? _transient;
    private bool _devicesInput; // the sound panel lists microphones rather than outputs
    private View? _panel;
    private bool _expanded;
    private bool _timerDone;
    private bool _levelsAnimating;
    private bool _microphoneMuted;
    private Brush _accent = Brushes.White;
    private string? _gifPath;
    private double _scale = 1;

    // The call app holding the microphone, and since when.
    private AppUse? _call;
    private DateTime _callSince;

    // Bluetooth devices being looked at (a speaker's endpoints connect together), and the last one announced.
    private readonly HashSet<Guid> _checkingBluetooth = [];
    private (Guid Container, bool Connected, DateTime At) _lastBluetooth;

    // The latest screenshot, saved as a temporary file to open or save.
    private string? _screenshotPath;

    // Optimize panel state: apps asked to close (name -> memory, to report what was freed),
    // and the app whose "terminate" button is waiting for a confirming second click.
    private readonly Dictionary<string, (string DisplayName, long Memory)> _closing = new(StringComparer.OrdinalIgnoreCase);
    private string? _armedApp;
    private bool _refreshAfterClose;

    internal IslandWindow(MediaService media, VolumeService volume, PowerService power, SystemStatsService stats,
        ProcessMonitor processes, PrivacyMonitor privacy, MicrophoneService microphone, TimerService timer,
        AppSettings settings)
    {
        InitializeComponent();
        _media = media;
        _volume = volume;
        _stats = stats;
        _processes = processes;
        _privacy = privacy;
        _microphone = microphone;
        _timer = timer;
        _settings = settings;

        var bars = LevelBars.Children.OfType<FrameworkElement>().ToArray();
        var levelScales = new ScaleTransform[bars.Length];
        for (int i = 0; i < bars.Length; i++)
        {
            levelScales[i] = new ScaleTransform(1, LevelRest[i]);
            bars[i].RenderTransform = levelScales[i];
            bars[i].RenderTransformOrigin = new Point(0.5, 1);
        }
        _levelMeter = new LevelMeter(levelScales, LevelRest, volume);

        _views = new Dictionary<View, FrameworkElement>
        {
            [View.Idle] = IdleView,
            [View.CompactMedia] = CompactMediaView,
            [View.CompactTimer] = CompactTimerView,
            [View.CompactCall] = CompactCallView,
            [View.ExpandedMedia] = ExpandedMediaView,
            [View.Home] = HomeView,
            [View.Optimize] = OptimizeView,
            [View.Devices] = DevicesView,
            [View.TimerPicker] = TimerPickerView,
            [View.Volume] = VolumeView,
            [View.Power] = PowerView,
            [View.Microphone] = MicrophoneView,
            [View.Output] = OutputView,
            [View.Notice] = NoticeView,
            [View.Screenshot] = ScreenshotView,
            [View.Notification] = NotificationView,
            [View.TimerDone] = TimerDoneView,
        };
        foreach (var view in _views.Values) view.RenderTransform = new ScaleTransform(1, 1);

        Island.Clip = _shape;
        HoverBridge.Clip = _bridge;
        _shapeAnimator = new SpringAnimator(4, ApplyShape, stiffness: 320, damping: 27);
        _shapeAnimator.JumpTo(ShapeOf(IdleView));
        _shapeTarget = RectOf(IdleView);

        _hoverTimer = CreateTimer(220, () => SetExpanded(true));
        _leaveTimer = CreateTimer(300, () => SetExpanded(false));
        _transientTimer = CreateTimer(1800, () =>
        {
            _transient = null;
            Refresh();
        });
        // Only runs while the island is expanded (clock, progress bar, system stats).
        _tickTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) =>
        {
            _tickTimer.Interval = TimeSpan.FromSeconds(1);
            UpdateLiveContent();
            UpdateStats();
        };
        // Only runs while the optimize panel is open.
        _optimizeTimer = new DispatcherTimer(DispatcherPriority.Background);
        _optimizeTimer.Tick += (_, _) =>
        {
            _optimizeTimer.Interval = TimeSpan.FromSeconds(2);
            RefreshOptimizeList(force: _refreshAfterClose);
            _refreshAfterClose = false;
        };
        _disarmTimer = CreateTimer(3000, () =>
        {
            _armedApp = null;
            RefreshOptimizeList(force: true);
        });
        // Only runs while a running timer is on screen, once per second as its display changes.
        _timerTick = new DispatcherTimer(DispatcherPriority.Background);
        _timerTick.Tick += (_, _) => UpdateTimerDisplay();
        // Only runs while a call is on screen, once per second.
        _callTick = new DispatcherTimer(DispatcherPriority.Background);
        _callTick.Tick += (_, _) => UpdateCallDisplay();

        // Root has no background, so it only sees the pointer over the island or the hover bridge.
        Root.MouseEnter += (_, _) =>
        {
            _leaveTimer.Stop();
            // A screenshot preview or a notification stays while the pointer is on it, and doesn't open the island.
            if (IsLingering)
            {
                _transientTimer.Stop();
                return;
            }
            if (!_expanded) _hoverTimer.Start();
        };
        Root.MouseLeave += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsLingering)
            {
                _transientTimer.Interval = TimeSpan.FromSeconds(2);
                _transientTimer.Start();
                return;
            }
            if (_expanded) _leaveTimer.Start();
        };
        // Back over the island itself: the area it gave up while shrinking needn't stay hoverable.
        Island.MouseEnter += (_, _) =>
        {
            if (_skirt.IsEmpty) return;
            _skirt = Rect.Empty;
            UpdateHoverBridge();
        };
        Root.MouseLeftButtonDown += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_transient == View.Notification) EndTransient(); // a click puts it away
            else if (_transient != View.Screenshot) SetExpanded(true);
        };
        Root.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            MenuRequested?.Invoke();
        };
        Root.MouseWheel += (_, e) =>
        {
            float step = _settings.WheelStepPercent / 100f;
            if (_settings.WheelControlsVolume) _volume.Nudge(e.Delta > 0 ? step : -step);
        };

        SkipBackButton.Click += async (_, _) => await _media.SkipByAsync(TimeSpan.FromSeconds(-_settings.SkipSeconds));
        SkipForwardButton.Click += async (_, _) => await _media.SkipByAsync(TimeSpan.FromSeconds(_settings.SkipSeconds));
        PreviousButton.Click += async (_, _) => await _media.SkipPreviousAsync();
        PlayPauseButton.Click += async (_, _) => await _media.TogglePlayPauseAsync();
        NextButton.Click += async (_, _) => await _media.SkipNextAsync();
        InitializeSeeking();

        HomeStats.OptimizeRequested += () => OpenPanel(View.Optimize);
        MediaStats.OptimizeRequested += () => OpenPanel(View.Optimize);
        foreach (var actions in new[] { HomeActions, MediaActions })
        {
            actions.TimerClicked += () => OpenPanel(View.TimerPicker);
            actions.OutputClicked += () =>
            {
                _devicesInput = false;
                OpenPanel(View.Devices);
            };
            actions.MicrophoneClicked += _microphone.ToggleMute;
        }
        OptimizeBackButton.Click += (_, _) => ClosePanel();
        DevicesBackButton.Click += (_, _) => ClosePanel();
        OutputsTab.Click += (_, _) => ShowDevices(input: false);
        InputsTab.Click += (_, _) => ShowDevices(input: true);
        TimerBackButton.Click += (_, _) => ClosePanel();

        HomeShortcuts.AddRequested += () => SettingsRequested?.Invoke(SettingsPage.Shortcuts);
        MediaShortcuts.AddRequested += () => SettingsRequested?.Invoke(SettingsPage.Shortcuts);
        HomeTimer.Attach(_timer);
        MediaTimer.Attach(_timer);
        foreach (int minutes in TimerPresetMinutes)
        {
            var preset = new Button
            {
                Style = (Style)FindResource("PillButton"),
                Content = Loc.F("Minutes", minutes),
                Tag = minutes,
                Height = 28,
                Margin = new Thickness(4, 0, 4, 8),
            };
            preset.Click += (_, _) =>
            {
                _timer.StartCountdown(TimeSpan.FromMinutes(minutes));
                ClosePanel();
            };
            TimerPresets.Children.Add(preset);
        }
        StopwatchButton.Click += (_, _) =>
        {
            _timer.StartStopwatch();
            ClosePanel();
        };
        TimerDoneButton.Click += (_, _) =>
        {
            _timerDone = false;
            Refresh();
        };
        ScreenshotThumb.MouseLeftButtonUp += (_, _) => OpenScreenshot();
        ScreenshotOpenButton.Click += (_, _) => OpenScreenshot();
        ScreenshotSaveButton.Click += (_, _) => SaveScreenshot();

        _media.Changed += OnMediaChanged;
        _volume.Changed += OnVolumeChanged;
        _volume.OutputChanged += OnOutputChanged;
        _volume.DeviceConnectionChanged += OnDeviceConnectionChanged;
        _notifications.Arrived += ShowNotification;
        _microphone.Changed += OnMicrophoneChanged;
        _timer.Changed += OnTimerChanged;
        _timer.Finished += OnTimerFinished;
        _privacy.Changed += UpdatePrivacy;
        _topWindows.Changed += Refresh;
        Closed += (_, _) =>
        {
            _topWindows.Dispose();
            _notifications.Dispose();
        };
        power.PowerSourceChanged += () => ShowPower(lowBattery: false);
        power.BatteryLow += () => ShowPower(lowBattery: true);
        _settings.PropertyChanged += (_, _) => ApplySettings();
        Loc.Changed += OnLanguageChanged;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(PositionIsland);
        InitializePlacement();
        IsVisibleChanged += (_, _) =>
        {
            // e.g. hidden behind a full-screen game
            UpdateLevelBars();
            UpdateTimerDisplay();
            UpdateCallDisplay();
        };

        _microphoneMuted = _microphone.IsMuted;
        ApplySettings();
        OnMediaChanged();
    }

    public event Action? MenuRequested;

    /// <summary>Open, counting down, in a call or waiting to be dismissed: not the moment to restart for an update.</summary>
    public bool IsBusy => _expanded || _timer.IsActive || _timerDone || _call is not null;

    /// <summary>A short message from the app itself, shown like the volume or charger messages.</summary>
    public void ShowNotice(string glyph, string text, int milliseconds = 3000)
    {
        NoticeGlyph.Text = glyph;
        NoticeText.Text = text;
        ShowTransient(View.Notice, milliseconds);
    }

    /// <summary>Something in the island leads to the settings, e.g. "+" next to the shortcuts.</summary>
    public event Action<SettingsPage>? SettingsRequested;

    // ---- State -------------------------------------------------------------------------------

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (expanded)
        {
            // Hovering wins over a pending volume/charger message.
            _transient = null;
            _transientTimer.Stop();
        }
        else
        {
            _panel = null;
            _skirt = Rect.Empty;
        }
        UpdateHoverBridge();
        Refresh();
    }

    /// <summary>Re-applies everything the settings window can change; called on every settings change.</summary>
    private void ApplySettings()
    {
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(_settings.HoverDelayMs, 1));
        _privacy.Enabled = _settings.ShowPrivacyIndicators || _settings.ShowCalls;
        _notifications.Enabled = _settings.ShowNotifications;
        _topWindows.Enabled = _settings.YieldToTopWindows;
        ApplyScale();
        _stats.IncludeGpu = _settings.ShowStats && _settings.ShowGpu;
        _stats.IncludeCpuTemperature = _settings.WantsCpuTemperature;

        bool stats = _settings.ShowStats, clock = _settings.ShowClock;
        HomeClock.Visibility = clock ? Visibility.Visible : Visibility.Collapsed;
        // Without the clock, the battery and quick actions are all the header has: centre them.
        HomeTools.HorizontalAlignment = clock ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        HomeStats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
        HomeStats.Configure(_settings, separated: true); // the quick actions are always above it
        MediaStats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
        MediaStats.Configure(_settings, separated: true);
        UpdateMicrophoneControls();
        foreach (var actions in new[] { HomeActions, MediaActions })
            actions.Choose(_settings.ShowTimerButton, _settings.ShowSoundButton, _settings.ShowMicButton);
        UpdateSkipButtons();

        var shortcuts = _settings.Shortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeShortcuts.Visibility = MediaShortcuts.Visibility = shortcuts;
        HomeShortcuts.Show(_settings.Shortcuts);
        MediaShortcuts.Show(_settings.Shortcuts);
        if (_settings.GifPath != _gifPath) LoadGif();

        // Newly enabled readings (e.g. the CPU temperature sensor) start with a fresh measurement window.
        if (stats && IsExpanded(_current)) _stats.Begin();
        UpdatePrivacy(); // the lights or the call indicator may have been turned on or off

        // The current view may have changed size, or may not be allowed any more.
        UpdateExpandedSizes();
        Refresh();
    }

    /// <summary>The expanded views' heights follow what they hold: clock, shortcuts, running timer, system stats.</summary>
    private void UpdateExpandedSizes()
    {
        bool stats = _settings.ShowStats, timer = _timer.IsActive, clock = _settings.ShowClock;
        bool shortcuts = _settings.Shortcuts.Count > 0;
        HomeView.Width = clock || stats || timer || shortcuts ? 380 : 200; // just the quick actions
        HomeView.Height = 28 + (clock ? 56 : 28) + (shortcuts ? 46 : 0) + (timer ? 46 : 0) + (stats ? 62 : 0);
        ExpandedMediaView.Width = MediaViewWidth();
        ExpandedMediaView.Height = (stats ? 216 : 156) + (shortcuts ? 44 : 0) + (timer ? 44 : 0);
        MorphTo(_views[_current]);
    }

    /// <summary>Texts written in code, as opposed to the {DynamicResource} ones, are written again.</summary>
    private void OnLanguageChanged()
    {
        foreach (var preset in TimerPresets.Children.OfType<Button>()) preset.Content = Loc.F("Minutes", (int)preset.Tag);
        HomeTimer.Render();
        MediaTimer.Render();
        UpdatePrivacy();
        UpdateLiveContent();
        if (_current == View.Optimize) OptimizeStatus.Text = Loc.T("OptimizeHint");
        if (_current == View.Devices) RefreshDevices();
        UpdateSkipButtons();
    }

    /// <summary>The GIF left of the player's controls, sized to the room there.</summary>
    private async void LoadGif()
    {
        _gifPath = _settings.GifPath;
        bool shown = await MediaGif.LoadAsync(_gifPath, GifSlot.Width, GifSlot.Height, GifSlotRadius);
        MediaGif.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandedSizes(); // the player may need more room beside it
    }

    private void OpenPanel(View panel)
    {
        if (panel == View.Devices) RefreshDevices(); // sized to the list before the island morphs
        _panel = panel;
        Refresh();
    }

    private void ClosePanel()
    {
        _panel = null;
        Refresh();
    }

    // ---- Sound devices -----------------------------------------------------------------------

    private void ShowDevices(bool input)
    {
        _devicesInput = input;
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        var devices = _devicesInput ? AudioDevices.GetInputs() : AudioDevices.GetOutputs();
        OutputsTab.Background = _devicesInput ? Brushes.Transparent : SelectedTabBrush;
        InputsTab.Background = _devicesInput ? SelectedTabBrush : Brushes.Transparent;
        OutputsTab.Foreground = _devicesInput ? SecondaryBrush : Brushes.White;
        InputsTab.Foreground = _devicesInput ? Brushes.White : SecondaryBrush;
        DevicesList.Children.Clear();
        foreach (var device in devices) DevicesList.Children.Add(CreateDeviceRow(device));
        if (devices.Count == 0)
        {
            DevicesList.Children.Add(new TextBlock
            {
                Text = Loc.T(_devicesInput ? "NoInputs" : "NoOutputs"),
                Margin = new Thickness(10, 12, 0, 0),
                Foreground = SecondaryBrush,
            });
        }

        // Header and margins, plus up to five rows; more scroll.
        DevicesView.Height = 60 + Math.Clamp(devices.Count, 1, DeviceRows) * 44;
        if (_current == View.Devices) MorphTo(DevicesView);
    }

    private Button CreateDeviceRow(AudioDevice device)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = (Style)FindResource("Icon");
        row.Children.Add(new TextBlock
        {
            Style = icon,
            Text = _devicesInput ? MicrophoneGlyphText : device.IsHeadphones ? HeadphonesGlyphText : SpeakerGlyphText,
            Width = 18,
            Margin = new Thickness(0, 0, 12, 0),
            Foreground = device.IsDefault ? Brushes.White : SecondaryBrush,
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = device.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        if (device.Description.Length > 0 && device.Description != device.Name)
        {
            text.Children.Add(new TextBlock
            {
                Text = device.Description,
                FontSize = 11,
                Foreground = TertiaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (device.IsDefault)
        {
            var check = new TextBlock { Style = icon, Text = CheckGlyphText, FontSize = 13, Margin = new Thickness(10, 0, 0, 0), Foreground = GreenBrush };
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
        }

        var button = new Button { Style = (Style)FindResource("ListRow"), Content = row };
        button.Click += (_, _) =>
        {
            if (!device.IsDefault && AudioDevices.SetDefault(device.Id)) RefreshDevices();
        };
        return button;
    }

    private async void OnOutputChanged()
    {
        if (_current == View.Devices) RefreshDevices();
        // Picked from the panel (or the island is open anyway): the panel / player already shows it.
        if (_expanded || !_settings.ShowVolume) return;
        if (AudioDevices.GetOutputs().FirstOrDefault(d => d.IsDefault) is not { } device) return;
        var container = AudioDevices.Find(device.Id)?.Container;
        // A Bluetooth device that has just been announced as connected: that notice names it already.
        if (container is { } announced && announced == _lastBluetooth.Container && _lastBluetooth.Connected
            && DateTime.UtcNow - _lastBluetooth.At < TimeSpan.FromSeconds(5)) return;

        ShowOutput(device, "", null);
        if (_settings.ShowBluetooth && container is { } id && await BluetoothBattery.ReadAsync(id) is (true, { } battery)
            && _transient == View.Output && OutputName.Text == device.Name)
            ShowOutputBattery(battery);
    }

    /// <summary>A Bluetooth headset or speaker connected or went away: its name, and its battery if it reports one.</summary>
    private async void OnDeviceConnectionChanged(string deviceId, bool connected)
    {
        if (!_settings.ShowBluetooth || _expanded || !AudioDevices.IsOutput(deviceId)) return;
        if (AudioDevices.Find(deviceId) is not var (device, container)) return;
        if (!_checkingBluetooth.Add(container)) return; // its other endpoint (e.g. hands-free) is being looked at
        try
        {
            var (bluetooth, battery) = await BluetoothBattery.ReadAsync(container);
            if (!bluetooth) return;
            if (_lastBluetooth.Container == container && _lastBluetooth.Connected == connected
                && DateTime.UtcNow - _lastBluetooth.At < TimeSpan.FromSeconds(5)) return;
            _lastBluetooth = (container, connected, DateTime.UtcNow);
            ShowOutput(device, Loc.T(connected ? "Connected" : "Disconnected"), connected ? battery : null, 3500);
            if (!connected || battery is not null) return;

            // The battery level often arrives a moment after the connection.
            await Task.Delay(2000);
            if ((await BluetoothBattery.ReadAsync(container)).Percent is { } later && _transient == View.Output
                && OutputName.Text == device.Name)
                ShowOutputBattery(later);
        }
        finally
        {
            _checkingBluetooth.Remove(container);
        }
    }

    private void ShowOutput(AudioDevice device, string status, int? battery, int milliseconds = 2500)
    {
        OutputGlyph.Text = device.IsHeadphones ? HeadphonesGlyphText : SpeakerGlyphText;
        OutputName.Text = device.Name;
        OutputName.ToolTip = device.Description;
        OutputStatus.Text = status;
        ShowOutputBattery(battery);
        ShowTransient(View.Output, milliseconds);
    }

    private void ShowOutputBattery(int? percent)
    {
        OutputBattery.Visibility = percent is null ? Visibility.Collapsed : Visibility.Visible;
        if (percent is not { } value) return;
        OutputBatteryText.Text = Loc.Percent(value / 100.0);
        OutputBatteryText.Foreground = value <= 20 ? RedBrush : Brushes.White;
        OutputBatteryGlyph.Level = value / 100.0;
    }

    // ---- Screenshots and notifications -------------------------------------------------------

    /// <summary>Lingers while the pointer is on it, and a hover doesn't open the island over it.</summary>
    private bool IsLingering => _transient is View.Screenshot or View.Notification;

    /// <summary>A screenshot just landed on the clipboard: a small preview to open or save it.</summary>
    private async void OnClipboardChanged()
    {
        if (!_settings.ShowScreenshots || _expanded || !Screenshots.IsFromScreenshotTool(WindowNative.GetClipboardOwner())) return;
        BitmapSource? image = Screenshots.ReadClipboard();
        // The tool may still have the clipboard open: a couple more tries.
        for (int attempt = 0; attempt < 2 && image is null; attempt++)
        {
            await Task.Delay(150);
            image = Screenshots.ReadClipboard();
        }
        if (image is null) return;

        // A small copy for the preview, so the full image isn't kept around.
        double scale = Math.Min(1, 128.0 / image.PixelWidth);
        var thumbnail = new WriteableBitmap(new TransformedBitmap(image, new ScaleTransform(scale, scale)));
        thumbnail.Freeze();
        ScreenshotThumb.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill }.Frozen();
        ScreenshotDetail.Text = Loc.T("ScreenshotCopied");
        ScreenshotSaveButton.IsEnabled = ScreenshotOpenButton.IsEnabled = false;
        ShowTransient(View.Screenshot, 6000);

        _screenshotPath = await Task.Run(() => Screenshots.SaveTemporary(image));
        ScreenshotSaveButton.IsEnabled = ScreenshotOpenButton.IsEnabled = _screenshotPath is not null;
    }

    private void OpenScreenshot()
    {
        if (_screenshotPath is null) return;
        Screenshots.Open(_screenshotPath);
        EndTransient();
    }

    private void SaveScreenshot()
    {
        if (_screenshotPath is null) return;
        bool saved = Screenshots.SaveToScreenshots(_screenshotPath, Loc.T("ScreenshotTaken")) is not null;
        ShowNotice(saved ? CheckGlyphText : "\uE783", Loc.T(saved ? "ScreenshotSaved" : "ScreenshotSaveFailed"), 2500);
    }

    /// <summary>A Windows notification, repeated on the island.</summary>
    private void ShowNotification(IslandNotification notification)
    {
        if (!_settings.ShowNotifications || _expanded || _timerDone || _transient == View.Screenshot) return;
        NotificationIcon.Source = notification.Icon;
        NotificationBell.Visibility = notification.Icon is null ? Visibility.Visible : Visibility.Collapsed;
        NotificationApp.Text = notification.App;
        NotificationTitle.Text = notification.Title;
        NotificationBody.Text = notification.Body;
        NotificationBody.Visibility = notification.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowTransient(View.Notification, 5000);
    }

    private void EndTransient()
    {
        _transientTimer.Stop();
        _transient = null;
        Refresh();
    }

    // ---- Microphone --------------------------------------------------------------------------

    /// <summary>Muted or unmuted from anywhere (the island, the shortcut, a headset button, Windows).</summary>
    private void OnMicrophoneChanged()
    {
        if (_current == View.Devices && _devicesInput) RefreshDevices(); // e.g. picked from Windows' own settings
        bool toggled = _microphone.IsMuted != _microphoneMuted;
        _microphoneMuted = _microphone.IsMuted;
        UpdateMicrophoneControls();
        UpdatePrivacy();
        // Open, the quick-action button already shows it.
        if (!toggled || !_microphone.IsAvailable || _expanded) return;

        bool muted = _microphone.IsMuted;
        MicrophoneGlyph.Text = muted ? MicrophoneOffGlyphText : MicrophoneGlyphText;
        MicrophoneGlyph.Foreground = muted ? RedBrush : Brushes.White;
        MicrophoneText.Text = Loc.T(muted ? "MicOff" : "MicOn");
        MicrophoneShortcut.Text = _settings.MicMuteHotkey;
        ShowTransient(View.Microphone);
    }

    private void UpdateMicrophoneControls()
    {
        foreach (var actions in new[] { HomeActions, MediaActions })
            actions.ShowMicrophone(_microphone.IsAvailable, _microphone.IsMuted, _settings.MicMuteHotkey);
    }

    // ---- Timer -------------------------------------------------------------------------------

    private void OnTimerChanged()
    {
        bool active = _timer.IsActive;
        var visibility = active ? Visibility.Visible : Visibility.Collapsed;
        HomeTimer.Visibility = MediaTimer.Visibility = CompactMediaTimer.Visibility = visibility;
        LevelBars.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        HomeActions.ShowTimer(active);
        MediaActions.ShowTimer(active);
        UpdateExpandedSizes();
        Refresh();
    }

    private void OnTimerFinished()
    {
        TimerDoneDetail.Text = Loc.F("TimerOfMinutes", Math.Round(_timer.Duration.TotalMinutes));
        _timerDone = true;
        if (_settings.TimerSound)
        {
            WindowNative.PlaySound(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav"),
                IntPtr.Zero, WindowNative.SND_ASYNC | WindowNative.SND_FILENAME | WindowNative.SND_NODEFAULT);
        }
        Refresh();
    }

    /// <summary>Redraws the running timer wherever it is on screen, and schedules the next redraw.</summary>
    private void UpdateTimerDisplay()
    {
        _timerTick.Stop();
        bool onScreen = IsVisible && (_current is View.CompactTimer or View.CompactMedia || IsExpanded(_current));
        if (!_timer.IsActive || !onScreen) return;

        CompactTimerText.Text = CompactMediaTimerText.Text = TimerService.Format(_timer.Display);
        CompactTimerText.Opacity = CompactMediaTimerText.Opacity = _timer.IsPaused ? 0.5 : 1;
        HomeTimer.Render();
        MediaTimer.Render();
        if (_timer.IsPaused) return;
        _timerTick.Interval = _timer.UntilNextChange;
        _timerTick.Start();
    }

    // ---- Calls -------------------------------------------------------------------------------

    /// <summary>A call app took the microphone (a call started) or let it go (it ended).</summary>
    private void UpdateCall()
    {
        var app = _settings.ShowCalls ? _privacy.MicrophoneUsers.FirstOrDefault(u => CallApps.Contains(u.Exe)) : null;
        if (app?.Exe == _call?.Exe) return;
        _call = app;
        _callSince = DateTime.Now;
        if (app is not null)
        {
            CallName.Text = app.Name;
            CompactCallView.ToolTip = Loc.F("InCall", app.Name);
        }
        Refresh();
    }

    /// <summary>The call's length, redrawn once a second while it's on screen.</summary>
    private void UpdateCallDisplay()
    {
        _callTick.Stop();
        if (_call is null || !IsVisible || _current != View.CompactCall) return;
        var length = DateTime.Now - _callSince;
        CallTime.Text = FormatTime(length);
        _callTick.Interval = TimeSpan.FromMilliseconds(1005 - length.Milliseconds);
        _callTick.Start();
    }

    // ---- Optimize panel ----------------------------------------------------------------------

    private void StartOptimize()
    {
        _processes.Begin();
        OptimizeList.Children.Clear();
        OptimizeSummary.Text = "";
        OptimizeStatus.Text = Loc.T("OptimizeHint");
        // The first list needs a short CPU measurement window; after that, every two seconds.
        _optimizeTimer.Interval = TimeSpan.FromMilliseconds(600);
        _optimizeTimer.Start();
    }

    private void StopOptimize()
    {
        _optimizeTimer.Stop();
        _disarmTimer.Stop();
        _armedApp = null;
        _closing.Clear();
    }

    private void RefreshOptimizeList(bool force = false)
    {
        if (_current != View.Optimize) return;
        // Don't reshuffle rows under the pointer; the user may be about to click one.
        if (!force && OptimizeList.IsMouseOver) return;

        var apps = _processes.Sample(OptimizeRows);
        var (ram, ramUsedGb) = SystemStatsService.ReadMemory();
        OptimizeSummary.Text = Loc.F("RamSummary", Loc.Percent(ram), ramUsedGb);

        // Report apps that have gone since we asked them to close.
        foreach (var name in _closing.Keys.ToList())
        {
            if (apps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || _processes.IsRunning(name)) continue;
            var (displayName, memory) = _closing[name];
            _closing.Remove(name);
            OptimizeStatus.Text = Loc.F("AppClosed", displayName, FormatBytes(memory));
        }

        OptimizeList.Children.Clear();
        foreach (var app in apps) OptimizeList.Children.Add(CreateAppRow(app));
    }

    private Grid CreateAppRow(AppUsage app)
    {
        var row = new Grid { Height = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 13,
        };
        name.Inlines.Add(new Run(app.DisplayName));
        if (app.ProcessCount > 1)
            name.Inlines.Add(new Run($"  ×{app.ProcessCount}") { Foreground = TertiaryBrush, FontSize = 11 });
        var icon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0),
            Source = app.Path is { } path ? AppIcons.ForPath(path, 32) : null,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var label = new DockPanel { Margin = new Thickness(0, 0, 8, 0), ToolTip = app.Path ?? $"{app.Name}.exe" };
        label.Children.Add(icon);
        label.Children.Add(name);
        row.Children.Add(label);

        var memory = new TextBlock
        {
            Text = FormatBytes(app.MemoryBytes),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            FontSize = 12,
            Foreground = SecondaryBrush,
        };
        Grid.SetColumn(memory, 1);
        row.Children.Add(memory);

        var cpu = new TextBlock
        {
            Text = app.CpuUsage >= 0.005 ? Loc.Percent(app.CpuUsage) : "",
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            FontSize = 12,
            Foreground = app.CpuUsage > 0.25 ? OrangeBrush : SecondaryBrush,
        };
        Grid.SetColumn(cpu, 2);
        row.Children.Add(cpu);

        // Polite close first (the app can ask to save); apps without a window, or ones that ignored
        // the polite request, get "terminate", which needs a confirming second click.
        bool polite = app.HasWindow && !_closing.ContainsKey(app.Name);
        bool armed = !polite && app.Name.Equals(_armedApp, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Style = (Style)FindResource("PillButton"),
            Content = Loc.T(polite ? "Close" : armed ? "AreYouSure" : "Terminate"),
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = Loc.T(polite ? "CloseTip" : "TerminateTip"),
        };
        if (armed) button.Background = DangerButtonBrush;
        button.Click += (_, _) => OnCloseApp(app, polite);
        Grid.SetColumn(button, 3);
        row.Children.Add(button);

        return row;
    }

    private void OnCloseApp(AppUsage app, bool polite)
    {
        if (polite)
        {
            ProcessMonitor.Close(app, force: false);
            _closing[app.Name] = (app.DisplayName, app.MemoryBytes);
            OptimizeStatus.Text = Loc.F("Closing", app.DisplayName);
        }
        else if (!app.Name.Equals(_armedApp, StringComparison.OrdinalIgnoreCase))
        {
            _armedApp = app.Name;
            _disarmTimer.Stop();
            _disarmTimer.Start();
            RefreshOptimizeList(force: true);
            return;
        }
        else
        {
            _armedApp = null;
            _disarmTimer.Stop();
            ProcessMonitor.Close(app, force: true);
            _closing[app.Name] = (app.DisplayName, app.MemoryBytes);
            OptimizeStatus.Text = Loc.F("Terminating", app.DisplayName);
        }

        // Give the app a moment to go away, then show the new picture.
        _refreshAfterClose = true;
        _optimizeTimer.Stop();
        _optimizeTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _optimizeTimer.Start();
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? $"{(bytes / 1073741824.0).ToString("0.0", Loc.Culture)} GB"
        : $"{bytes >> 20} MB";

    private void ShowTransient(View view, int milliseconds = 1800)
    {
        _transient = view;
        _transientTimer.Stop();
        _transientTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _transientTimer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var next = ChooseView();
        bool wasExpanded = IsExpanded(_current);
        bool wasOptimizing = _current == View.Optimize;
        if (next != _current) SwitchTo(next);

        if (!IsExpanded(_current))
        {
            _tickTimer.Stop();
        }
        else if (!wasExpanded)
        {
            // Start a fresh measurement window; the first reading lands shortly after the morph.
            if (_settings.ShowStats) _stats.Begin();
            HomeStats.Clear();
            MediaStats.Clear();
            _tickTimer.Interval = TimeSpan.FromMilliseconds(400);
            _tickTimer.Start();
        }

        if (_current == View.Optimize && !wasOptimizing) StartOptimize();
        else if (_current != View.Optimize && wasOptimizing) StopOptimize();

        UpdateLiveContent();
        UpdateLevelBars();
        UpdateIdleLiveLine();
        UpdateTimerDisplay();
        UpdateCallDisplay();
    }

    private View ChooseView()
    {
        if (_timerDone) return View.TimerDone; // until dismissed, even while hovering
        if (_transient is { } transient) return transient;
        if (_expanded)
        {
            if (_panel is { } panel) return panel;
            if (_settings.ShowMedia && _media.HasMedia) return View.ExpandedMedia;
            return View.Home; // at least the quick actions
        }
        // A window reaching up under the island (a maximised browser's tabs) gets the space back: the
        // compact view shrinks to the resting tab, which shows a thin "something is live" line instead.
        if (_topWindows.IsCovered) return View.Idle;
        if (_call is not null) return View.CompactCall;
        if (_settings.ShowMedia && _media.IsPlaying) return View.CompactMedia; // with the timer, if one runs
        return _timer.IsActive ? View.CompactTimer : View.Idle;
    }

    /// <summary>
    /// The resting tab's thin line: a call, music playing or a timer running, while the compact view has
    /// stepped aside.
    /// </summary>
    private void UpdateIdleLiveLine()
    {
        bool call = _call is not null, playing = _settings.ShowMedia && _media.IsPlaying;
        IdleLiveLine.Visibility = _current == View.Idle && (call || playing || _timer.IsActive) ? Visibility.Visible : Visibility.Collapsed;
        IdleLiveLine.Background = call ? GreenBrush : playing ? _accent : OrangeBrush;
    }

    private static bool IsExpanded(View view) => view is View.Home or View.ExpandedMedia;

    private void UpdateStats()
    {
        if (!_settings.ShowStats) return;
        var stats = _stats.Sample();
        HomeStats.Show(stats);
        MediaStats.Show(stats);
    }

    /// <summary>The level bars move only while they are actually on screen and music is playing.</summary>
    private void UpdateLevelBars()
    {
        bool animate = IsVisible && _settings.AnimateLevelBars && _current == View.CompactMedia && _media.IsPlaying
            && !_timer.IsActive; // the timer has their place
        if (animate == _levelsAnimating) return;
        _levelsAnimating = animate;
        if (animate) _levelMeter.Start();
        else _levelMeter.Stop();
    }

    // ---- Microphone / camera indicators ------------------------------------------------------

    /// <summary>
    /// Orange dot: an app is using the microphone; green: the camera. Shown in every state: the resting
    /// tab grows a little to hold them, the compact player has them next to the level bars, and the
    /// larger states carry them in their top padding.
    /// </summary>
    private void UpdatePrivacy()
    {
        // The monitor also runs for the call indicator alone; the lights show only when they're on.
        bool lights = _settings.ShowPrivacyIndicators;
        IReadOnlyList<string> microphone = lights ? _privacy.MicrophoneApps : [];
        IReadOnlyList<string> camera = lights ? _privacy.CameraApps : [];
        bool any = microphone.Count > 0 || camera.Count > 0;
        // In a call but muted: the app still holds the microphone, it just gets silence.
        bool muted = microphone.Count > 0 && _microphone.IsMuted;

        MicDot.Visibility = CompactMicDot.Visibility = microphone.Count > 0 && !muted ? Visibility.Visible : Visibility.Collapsed;
        MicMutedMark.Visibility = CompactMicMutedMark.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = CompactCameraDot.Visibility = camera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactPrivacy.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        var lines = new List<string>(2);
        if (microphone.Count > 0) lines.Add(Loc.F(muted ? "MicrophoneMutedApps" : "MicrophoneApps", string.Join(", ", microphone)));
        if (camera.Count > 0) lines.Add(Loc.F("CameraApps", string.Join(", ", camera)));
        PrivacyDots.ToolTip = CompactPrivacy.ToolTip = any ? string.Join(Environment.NewLine, lines) : null;

        // Resting tab: 6 px showing normally, 18 px with the dots (still hanging, so its top stays flat).
        IdleView.Height = any ? 36 : 10;
        IdleView.Margin = new Thickness(0, any ? -18 : -4, 0, 0);
        if (_current == View.Idle) MorphTo(IdleView);
        PlacePrivacyDots();

        // The call view has its own marks: muted, and the camera on.
        CallMutedMark.Visibility = _microphone.IsMuted ? Visibility.Visible : Visibility.Collapsed;
        CallCameraDot.Visibility = _privacy.CameraApps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCall();
    }

    private void PlacePrivacyDots()
    {
        bool any = _settings.ShowPrivacyIndicators && (_privacy.MicrophoneApps.Count > 0 || _privacy.CameraApps.Count > 0);
        PrivacyDots.Visibility = any && _current is not (View.CompactMedia or View.CompactCall) ? Visibility.Visible : Visibility.Collapsed;

        // Centred in the visible part of the resting tab and the compact timer (the gap between icon and
        // time), otherwise in the top padding. Dots outside the island's current shape are clipped, so
        // they show up as the shape morphs over them.
        var view = _views[_current];
        double top = view.Margin.Top;
        double y = _current is View.Idle or View.CompactTimer ? (Math.Max(top, 0) + top + view.Height) / 2 - 3 : top + 5;
        PrivacyOffset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseOut });
    }

    private void SwitchTo(View next)
    {
        var outgoing = _views[_current];
        var incoming = _views[next];
        _current = next;

        outgoing.IsHitTestVisible = false;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            // Collapsed panels cost nothing to lay out or render.
            if (!ReferenceEquals(_views[_current], outgoing)) outgoing.Visibility = Visibility.Collapsed;
        };
        outgoing.BeginAnimation(OpacityProperty, fadeOut);

        incoming.Visibility = Visibility.Visible;
        incoming.IsHitTestVisible = true;
        var delay = TimeSpan.FromMilliseconds(60);
        incoming.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = EaseOut });
        var grow = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = delay, EasingFunction = EaseOut };
        var scale = (ScaleTransform)incoming.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        MorphTo(incoming);
        PlacePrivacyDots();
    }

    /// <summary>
    /// Morphs the island to a view's shape. When an open island shrinks out from under the pointer (e.g. the
    /// player's output button opens a shorter panel), that would count as leaving and close it; so the area
    /// it gave up stays hoverable until the pointer moves away from it or back onto the island.
    /// </summary>
    private void MorphTo(FrameworkElement view)
    {
        var target = RectOf(view);
        if (_expanded && Root.IsMouseOver && !target.Contains(Mouse.GetPosition(Root)))
            _skirt = _skirt.IsEmpty ? _shapeTarget : Rect.Union(_skirt, _shapeTarget);
        _shapeTarget = target;
        _shapeAnimator.AnimateTo(ShapeOf(view));
        UpdateHoverBridge();
    }

    // Width, height, corner radius and top offset of the island for a given view.
    private static double[] ShapeOf(FrameworkElement view) =>
        [view.Width, view.Height, Math.Min(view.Height / 2, MaxRadius), view.Margin.Top];

    private static Rect RectOf(FrameworkElement view) => new((BaseWidth - view.Width) / 2, view.Margin.Top, view.Width, view.Height);

    private void ApplyShape(double[] values)
    {
        double width = Math.Max(values[0], 0);
        double height = Math.Max(values[1], 0);
        double radius = Math.Clamp(values[2], 0, Math.Min(width, height) / 2);
        _shape.Rect = new Rect((BaseWidth - width) / 2, values[3], width, height);
        _shape.RadiusX = radius;
        _shape.RadiusY = radius;
        UpdateHoverBridge();
    }

    /// <summary>The window and everything in it at the chosen island size; the views keep their sizes in island units.</summary>
    private void ApplyScale()
    {
        double scale = _settings.IslandScale;
        if (scale == _scale) return;
        _scale = scale;
        Root.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        Width = BaseWidth * scale;
        Height = BaseHeight * scale;
        Dispatcher.InvokeAsync(PositionIsland, DispatcherPriority.Loaded);
    }

    private void UpdateHoverBridge()
    {
        var shape = _shape.Rect;
        if (!_expanded || shape.IsEmpty)
        {
            _bridge.Rect = Rect.Empty;
            return;
        }
        var gap = new Rect(shape.X, 0, shape.Width, Math.Max(shape.Y, 0) + 1);
        _bridge.Rect = _skirt.IsEmpty ? gap : Rect.Union(gap, _skirt);
    }

    // ---- Content -----------------------------------------------------------------------------

    private void OnMediaChanged()
    {
        CompactTitle.Text = _media.Title;
        MediaTitle.Text = _media.Title;
        MediaArtist.Text = _media.Artist;

        Brush art = _media.Artwork is { } image
            ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }.Frozen()
            : ArtPlaceholder;
        CompactArt.Background = art;
        MediaArt.Background = art;

        var accent = new SolidColorBrush(_media.Accent).Frozen();
        foreach (var bar in LevelBars.Children.OfType<System.Windows.Shapes.Shape>()) bar.Fill = accent;
        ProgressFill.Background = accent;
        _accent = accent;

        PlayPauseIcon.Data = _media.IsPlaying ? PauseGlyph : PlayGlyph;
        UpdateSourceIcon();
        UpdateSkipButtons();
        Refresh();
    }

    private void OnVolumeChanged()
    {
        if (!_settings.ShowVolume) return;
        bool muted = _volume.IsMuted;
        VolumeText.Text = Loc.Percent(_volume.Level);
        VolumeText.Opacity = muted ? 0.5 : 1;
        VolumeWaves.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
        MuteMark.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        VolumeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(muted ? 0 : _volume.Level, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut });
        ShowTransient(View.Volume);
    }

    private void ShowPower(bool lowBattery)
    {
        if (!_settings.ShowPower) return;
        var status = PowerService.GetStatus();
        if (!status.HasBattery) return;

        PowerGlyph.Level = status.Percent / 100.0;
        PowerGlyph.IsCharging = status.OnAc;
        PowerText.Text = Loc.T(lowBattery ? "BatteryLow"
            : status.OnAc ? (status.Percent >= 100 ? "FullyCharged" : "Charging")
            : "OnBattery");
        PowerPercent.Text = Loc.Percent(status.Percent / 100.0);
        PowerPercent.Foreground = status.OnAc ? GreenBrush : status.Percent <= 20 ? RedBrush : Brushes.White;
        ShowTransient(View.Power);
    }

    private void UpdateLiveContent()
    {
        if (_current == View.Home)
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm", Loc.Culture);
            DateText.Text = now.ToString(Loc.T("DateFormat"), Loc.Culture);

            var status = PowerService.GetStatus();
            HomeBattery.Visibility = status.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            HomeBatteryText.Text = Loc.Percent(status.Percent / 100.0);
            HomeBatteryGlyph.Level = status.Percent / 100.0;
            HomeBatteryGlyph.IsCharging = status.OnAc;
        }
        else if (_current == View.ExpandedMedia)
        {
            var timeline = _media.GetTimeline();
            ProgressRow.Visibility = timeline is null ? Visibility.Hidden : Visibility.Visible;
            if (timeline is { } t && !_seeking) // While dragging, the bar shows the pointer.
            {
                ProgressScale.ScaleX = t.Position / t.Duration;
                ElapsedText.Text = FormatTime(t.Position);
                RemainingText.Text = "-" + FormatTime(t.Duration - t.Position);
            }
        }
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    // ---- Window plumbing ---------------------------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        // Tool window: not in Alt+Tab. No-activate: clicking the island never steals focus.
        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        // Windows says when the clipboard changes (no polling); only screenshots are of interest.
        WindowNative.AddClipboardFormatListener(handle);
        PositionIsland();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE || (msg == WM_SETTINGCHANGE && (int)wParam == SPI_SETWORKAREA))
            Dispatcher.InvokeAsync(PositionIsland);
        else if (msg == WindowNative.WM_CLIPBOARDUPDATE)
            Dispatcher.InvokeAsync(OnClipboardChanged);
        return IntPtr.Zero;
    }

    private static DispatcherTimer CreateTimer(int milliseconds, Action onElapsed)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            onElapsed();
        };
        return timer;
    }
}
