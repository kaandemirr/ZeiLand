using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZeiLand.Services;
using ZeiLand.Settings;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand;

/// <summary>Everything the user can change. Bound straight to <see cref="AppSettings"/>, so changes apply live.</summary>
public partial class SettingsWindow : Window
{
    private const string PawnIoPackage = "namazso.PawnIO";
    private const string PawnIoDownloadPage = "https://github.com/namazso/PawnIO.Setup/releases/latest";

    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)).Frozen();
    private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)).Frozen();
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush SecondaryBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)).Frozen();

    private readonly AppSettings _settings;
    private readonly SystemStatsService _stats;
    private readonly MediaService _media;
    private readonly Hotkeys _hotkeys;
    private readonly UpdateService _updates;
    private readonly DispatcherTimer _statusTimer;
    private bool _installingDriver;
    private bool _recordingShortcut;

    internal SettingsWindow(AppSettings settings, SystemStatsService stats, MediaService media, Hotkeys hotkeys,
        UpdateService updates, Action restartElevated, Action showWelcome, Action previewAod)
    {
        InitializeComponent();
        _settings = settings;
        _stats = stats;
        _media = media;
        MaxHeight = SystemParameters.WorkArea.Height - 16;
        _hotkeys = hotkeys;
        _updates = updates;
        DataContext = settings;

        InitializeTabs();
        InitializeMediaSources();
        InitializeAppearance();
        InitializeAod(previewAod);
        InitializeShortcuts();
        InitializeAbout(showWelcome);

        UpdateShortcut();
        ShortcutButton.Click += (_, _) =>
        {
            if (_recordingShortcut) StopRecordingShortcut();
            else StartRecordingShortcut();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => StopRecordingShortcut();

        foreach (var option in HoverDelayOptions.Children.OfType<RadioButton>())
        {
            option.IsChecked = int.Parse((string)option.Tag) == settings.HoverDelayMs;
            option.Checked += (_, _) => _settings.HoverDelayMs = int.Parse((string)option.Tag);
        }
        foreach (var option in WheelStepOptions.Children.OfType<RadioButton>())
        {
            option.Content = Loc.Percent(int.Parse((string)option.Tag) / 100.0);
            option.IsChecked = int.Parse((string)option.Tag) == settings.WheelStepPercent;
            option.Checked += (_, _) => _settings.WheelStepPercent = int.Parse((string)option.Tag);
        }
        foreach (var option in LanguageOptions.Children.OfType<RadioButton>())
        {
            option.IsChecked = (string)option.Tag == (Loc.IsEnglish ? "en" : "tr");
            option.Checked += (_, _) => _settings.Language = (string)option.Tag;
        }
        Loc.Changed += WriteTexts;

        AutostartToggle.IsChecked = Autostart.IsEnabled;
        AutostartToggle.Click += OnAutostartClick;
        InstallDriverButton.Click += OnInstallDriverClick;
        RestartElevatedButton.Click += (_, _) => restartElevated();

        // Only while the window is open: the sensor may still be starting, or the driver being installed.
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => UpdateCpuTemperatureStatus();
        _settings.PropertyChanged += OnSettingsChanged;

        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += (_, _) =>
        {
            UpdateCpuTemperatureStatus();
            _statusTimer.Start();
        };
        Closed += (_, _) =>
        {
            StopRecordingShortcut();
            _statusTimer.Stop();
            _settings.PropertyChanged -= OnSettingsChanged;
            Loc.Changed -= WriteTexts;
            _updates.Changed -= UpdateUpdateStatus;
        };
    }

    /// <summary>After a language change: the texts written in code (the rest follow by themselves).</summary>
    private void WriteTexts()
    {
        UpdateShortcut();
        UpdateCpuTemperatureStatus();
        RenderShortcuts();
        RenderMediaSources();
        RenderAod();
        RenderSkip();
        UpdatePosition();
        _ = ShowGif();
        UpdateUpdateStatus();
        VersionText.Text = Loc.T("SettingsSaved");
        ShortcutStatus.Text = "";
        foreach (var option in WheelStepOptions.Children.OfType<RadioButton>())
            option.Content = Loc.Percent(int.Parse((string)option.Tag) / 100.0);
        if (AppPicker.IsVisible) FilterApps();
    }

    // ---- Microphone shortcut -----------------------------------------------------------------

    private void StartRecordingShortcut()
    {
        _recordingShortcut = true;
        _hotkeys.Suspended = true; // otherwise the current shortcut would swallow its own key press
        ShortcutButton.Tag = "Recording";
        ShortcutButton.Content = Loc.T("PressKeys");
        ShortcutDescription.Text = Loc.T("RecordingHint");
        ShortcutDescription.Foreground = SecondaryBrush;
    }

    private void StopRecordingShortcut()
    {
        if (!_recordingShortcut) return;
        _recordingShortcut = false;
        _hotkeys.Suspended = false;
        ShortcutButton.Tag = null;
        UpdateShortcut();
    }

    private void UpdateShortcut()
    {
        string shortcut = _settings.MicMuteHotkey;
        ShortcutButton.Content = shortcut.Length > 0 ? shortcut.Replace("+", " + ") : Loc.T("None");
        bool taken = shortcut.Length > 0 && !_hotkeys.MicrophoneRegistered;
        ShortcutDescription.Text = taken
            ? Loc.T("ShortcutTaken")
            : Loc.T("MicShortcutTip");
        ShortcutDescription.Foreground = taken ? RedBrush : SecondaryBrush;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingShortcut) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt combinations arrive as "System"
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            StopRecordingShortcut();
            return;
        }
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            _settings.MicMuteHotkey = "";
            StopRecordingShortcut();
            return;
        }
        if (Hotkeys.Format(modifiers, key) is not { } shortcut) return; // only modifiers so far
        _settings.MicMuteHotkey = shortcut;
        StopRecordingShortcut();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Shortcuts):
                RenderShortcuts();
                break;
            case nameof(AppSettings.HiddenMediaApps):
                RenderMediaSources();
                break;
            case nameof(AppSettings.AodTrigger) or nameof(AppSettings.AodIdleMinutes) or nameof(AppSettings.AodScreen):
                RenderAod();
                break;
            case nameof(AppSettings.SkipSeconds):
                RenderSkip();
                break;
            case nameof(AppSettings.YieldToTopWindows):
                UpdateRestingStyle();
                break;
            case nameof(AppSettings.IslandMonitor) or nameof(AppSettings.IslandPosition):
                UpdatePosition();
                break;
            case nameof(AppSettings.GifPath):
                _ = ShowGif();
                break;
            default:
                UpdateCpuTemperatureStatus();
                break;
        }
    }

    private void UpdateCpuTemperatureStatus()
    {
        var reader = _stats.CpuTemperature;
        if (_settings.WantsCpuTemperature) reader.EnsureStarted();

        bool install = false, restart = false;
        (string title, string detail, Brush dot) status;
        if (!_settings.WantsCpuTemperature)
        {
            status = (Loc.T("Off"), Loc.T("CpuTempOffTip"), GrayBrush);
        }
        else if (_installingDriver)
        {
            status = (Loc.T("InstallingPawnIO"), Loc.T("FinishSteps"), OrangeBrush);
        }
        else
        {
            switch (reader.Status)
            {
                case CpuTemperatureStatus.NeedsDriver:
                    install = true;
                    status = (Loc.T("DriverNeeded"), Loc.T("DriverNeededTip"), OrangeBrush);
                    break;
                case CpuTemperatureStatus.NeedsAdmin:
                    restart = true;
                    status = (Loc.T("AdminNeeded"), Loc.T("AdminNeededTip"), OrangeBrush);
                    break;
                case CpuTemperatureStatus.Available:
                    var temperature = reader.Read();
                    status = (temperature is { } t ? Loc.F("WorkingTemperature", t) : Loc.T("Working"), Loc.T("WorkingTip"), GreenBrush);
                    break;
                case CpuTemperatureStatus.Unsupported:
                    status = (Loc.T("Unreadable"), Loc.T("UnreadableTip"), RedBrush);
                    break;
                default:
                    status = (Loc.T("SensorStarting"), "", GrayBrush);
                    break;
            }
        }

        CpuTemperatureTitle.Text = status.title;
        CpuTemperatureDetail.Text = status.detail;
        CpuTemperatureDetail.Visibility = status.detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CpuTemperatureDot.Fill = status.dot;
        InstallDriverButton.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        RestartElevatedButton.Visibility = restart ? Visibility.Visible : Visibility.Collapsed;
        CpuTemperatureActions.Visibility = install || restart ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnInstallDriverClick(object sender, RoutedEventArgs e)
    {
        _installingDriver = true;
        UpdateCpuTemperatureStatus();
        try
        {
            // Visible console so the user sees what is installed; it stays open only if something failed.
            var info = new ProcessStartInfo("cmd.exe",
                $"/c winget install --id {PawnIoPackage} --exact --source winget || pause")
            {
                UseShellExecute = true,
            };
            using var process = Process.Start(info);
            if (process is not null) await process.WaitForExitAsync();
            if (!CpuTemperatureReader.IsDriverInstalled && process?.ExitCode != 0) OpenDownloadPage();
        }
        catch (Win32Exception)
        {
            OpenDownloadPage();
        }
        finally
        {
            _installingDriver = false;
            UpdateCpuTemperatureStatus();
        }
    }

    private static void OpenDownloadPage() =>
        Process.Start(new ProcessStartInfo(PawnIoDownloadPage) { UseShellExecute = true })?.Dispose();

    private async void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        bool wanted = AutostartToggle.IsChecked == true;
        AutostartToggle.IsEnabled = false;
        // May show a UAC prompt; don't freeze the window meanwhile.
        bool done = await Task.Run(() => Autostart.SetEnabled(wanted));
        AutostartToggle.IsChecked = done ? wanted : !wanted;
        AutostartToggle.IsEnabled = true;
    }

    private void UseDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        int caption = 0x00202020; // COLORREF of the window background
        DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
    }
}
