using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZeiLand.Native;
using ZeiLand.Services;
using ZeiLand.Settings;

namespace ZeiLand;

public partial class App : Application
{
    private const string EnableAutostartArgument = "--enable-autostart";
    private const string DisableAutostartArgument = "--disable-autostart";

    private Mutex? _instanceMutex;
    private HwndSource? _host;
    private MediaService? _media;
    private VolumeService? _volume;
    private PowerService? _power;
    private FullscreenWatcher? _fullscreen;
    private TrayIcon? _tray;
    private SystemStatsService? _stats;
    private PrivacyMonitor? _privacy;
    private MicrophoneService? _microphone;
    private Hotkeys? _hotkeys;
    private SettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private WelcomeWindow? _welcome;
    private UpdateService? _updates;
    private AlwaysOnDisplay? _aod;
    private TrayMenu? _menu;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Used by the installer, which already runs elevated: set up or remove the logon task and exit.
        if (e.Args.Contains(EnableAutostartArgument) || e.Args.Contains(DisableAutostartArgument))
        {
            bool done = Autostart.SetEnabled(e.Args.Contains(EnableAutostartArgument));
            Shutdown(done ? 0 : 1);
            return;
        }

        if (!AcquireInstanceMutex(waitForPrevious: e.Args.Contains(Elevation.RestartArgument)))
        {
            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();
        var settings = _settingsStore.Current;

        // First of the settings' listeners, so everything after it already writes in the new language.
        Loc.Apply(settings.Language);
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.Language)) Loc.Apply(settings.Language);
        };

        // Up to 0.6.0 the thin line was the default resting style; since then the mini player is.
        if (settings.LastRunVersion is null || (Version.TryParse(settings.LastRunVersion, out var lastRun) && lastRun < new Version(0, 6, 1)))
            settings.YieldToTopWindows = false;

        // The CPU temperature needs administrator mode. Autostart's logon task already runs elevated;
        // a manual start asks once through UAC, and carries on without the temperature if declined.
        if (!Elevation.IsElevated && settings.WantsCpuTemperature && CpuTemperatureReader.IsDriverInstalled
            && !e.Args.Contains(Elevation.RestartArgument) && RestartElevated())
        {
            return;
        }

        // The island is small and mostly static. Software rendering keeps the Direct3D stack and
        // GPU driver out of the process, which is the bulk of a WPF app's memory.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };

        // Hidden window that receives tray, power and app-bar messages.
        _host = new HwndSource(new HwndSourceParameters("ZeiLand Host")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
        });

        _media = new MediaService(Dispatcher) { HiddenApps = settings.HiddenMediaApps };
        _volume = new VolumeService(Dispatcher);
        _power = new PowerService(_host);
        _fullscreen = new FullscreenWatcher(_host);
        _tray = new TrayIcon(_host);
        _stats = new SystemStatsService();
        _privacy = new PrivacyMonitor(Dispatcher);
        _microphone = new MicrophoneService(Dispatcher);
        _microphone.Initialize(); // before the island, so its starting state isn't announced as a change

        _hotkeys = new Hotkeys(_host);
        _hotkeys.MicrophonePressed += _microphone.ToggleMute;
        _hotkeys.SetMicrophone(settings.MicMuteHotkey);

        var timer = new TimerService();
        var island = new IslandWindow(_media, _volume, _power, _stats, new ProcessMonitor(), _privacy, _microphone,
            timer, settings);
        _aod = new AlwaysOnDisplay(settings, _media, timer, _privacy, _power, _fullscreen, Dispatcher);
        island.MenuRequested += ShowMenu;
        island.SettingsRequested += page => ShowSettings(page);
        _tray.MenuRequested += ShowMenu;
        _tray.SettingsRequested += () => ShowSettings(null);

        // Updates install only at a quiet moment: no full-screen game, nothing open or counting down.
        _updates = new UpdateService(settings, () => !_fullscreen.IsFullscreen && !island.IsBusy, _settingsStore.Flush);
        _updates.Installing += version => island.ShowNotice("", Loc.F("Updating", version), 4000);
        _updates.Start();

        // A game in exclusive full screen (e.g. League of Legends' "Full screen" mode) shows nothing over it
        // whatever we do; in a borderless one the island stays on top.
        void UpdateIslandVisibility()
        {
            if (_fullscreen.IsFullscreen && settings.HideInFullscreen)
            {
                island.Hide();
                return;
            }
            island.Show();
            if (_fullscreen.IsFullscreen) island.BringToTop();
        }
        _fullscreen.Changed += _ => UpdateIslandVisibility();
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.HideInFullscreen)) UpdateIslandVisibility();
            else if (args.PropertyName == nameof(AppSettings.MicMuteHotkey)) _hotkeys.SetMicrophone(settings.MicMuteHotkey);
            else if (args.PropertyName == nameof(AppSettings.HiddenMediaApps)) _media.HiddenApps = settings.HiddenMediaApps;
        };

        MainWindow = island;
        island.Show();

        _volume.Initialize();
        _ = _media.InitializeAsync();
        _aod.Start();

        // After the first install: the welcome guide. After an update: a word from the island.
        string version = UpdateService.CurrentVersion;
        bool updated = settings.LastRunVersion is { } last && last != version;
        settings.LastRunVersion = version;
        After(TimeSpan.FromSeconds(1.5), () =>
        {
            if (!settings.WelcomeShown) ShowWelcome();
            else if (updated) island.ShowNotice("", Loc.F("Updated", version), 3500);
        });
    }

    private void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void ShowWelcome()
    {
        if (_welcome is null)
        {
            _welcome = new WelcomeWindow(_settingsStore!.Current);
            _welcome.Closed += (_, _) => _welcome = null;
            _welcome.Show();
        }
        _welcome.Activate();
    }

    private void ShowMenu()
    {
        _menu?.Close();
        _menu = new TrayMenu(Autostart.IsEnabled, _settingsStore!.Current.AodEnabled);
        _menu.AodToggled += enabled => _settingsStore.Current.AodEnabled = enabled;
        _menu.SettingsClicked += () => ShowSettings(null);
        _menu.AutostartToggled += enabled => Task.Run(() => Autostart.SetEnabled(enabled)); // may show UAC
        _menu.ExitClicked += Shutdown;
        _menu.Closed += (sender, _) =>
        {
            if (ReferenceEquals(_menu, sender)) _menu = null;
        };
        _menu.ShowAtPointer();
    }

    private void ShowSettings(SettingsPage? page)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsStore!.Current, _stats!, _media!, _hotkeys!, _updates!,
                () => RestartElevated(), ShowWelcome, () => _aod?.Preview());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (page is { } wanted) _settingsWindow.ShowPage(wanted);
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Replaces this instance with an elevated one. False (and nothing changes) if UAC was declined.</summary>
    private bool RestartElevated()
    {
        _settingsStore?.Flush();
        if (!Elevation.StartElevatedInstance()) return false;
        Shutdown();
        return true;
    }

    /// <summary>
    /// One island per session. A restarting instance waits for the one it replaces to exit. An elevated
    /// instance's mutex can't even be opened from a normal one, which also means "already running".
    /// </summary>
    private bool AcquireInstanceMutex(bool waitForPrevious)
    {
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ZeiLand", out bool createdNew);
            if (createdNew) return true;
            if (waitForPrevious && _instanceMutex.WaitOne(TimeSpan.FromSeconds(10))) return true;
        }
        catch (AbandonedMutexException)
        {
            return true; // The previous instance died without releasing it; it's ours now.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsWindow?.Close();
        _aod?.Dispose();
        _settingsStore?.Flush();
        _tray?.Dispose();
        _stats?.Dispose();
        _privacy?.Dispose();
        _hotkeys?.Dispose();
        _microphone?.Dispose();
        _fullscreen?.Dispose();
        _power?.Dispose();
        _volume?.Dispose();
        _media?.Dispose();
        _host?.Dispose();

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void Log(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeiLand");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"[{DateTime.Now:O}] {exception}\n\n");
        }
        catch (IOException)
        {
        }
    }
}
