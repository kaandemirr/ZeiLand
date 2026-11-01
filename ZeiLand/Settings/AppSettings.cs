using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ZeiLand.Settings;

/// <summary>User preferences. Changes are saved automatically and applied live.</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    // ---- What the island shows ----

    public bool ShowMedia { get; set => Set(ref field, value); } = true;
    public bool AnimateLevelBars { get; set => Set(ref field, value); } = true;

    /// <summary>Players (app ids, e.g. "Spotify.exe", "MSEdge") whose media isn't shown. Replaced as a whole on every change.</summary>
    public IReadOnlyList<string> HiddenMediaApps { get; set => Set(ref field, value ?? []); } = [];
    public bool ShowVolume { get; set => Set(ref field, value); } = true;
    public bool ShowPower { get; set => Set(ref field, value); } = true;
    public bool ShowPrivacyIndicators { get; set => Set(ref field, value); } = true;
    public bool ShowClock { get; set => Set(ref field, value); } = true;
    public bool ShowStats { get; set => Set(ref field, value); } = true;
    public bool ShowCpu { get; set => Set(ref field, value); } = true;
    public bool ShowCpuTemperature { get; set => Set(ref field, value); } = true;
    public bool ShowGpu { get; set => Set(ref field, value); } = true;
    public bool ShowGpuTemperature { get; set => Set(ref field, value); } = true;
    public bool ShowRam { get; set => Set(ref field, value); } = true;

    /// <summary>A Bluetooth headset or speaker connecting / disconnecting, with its battery when it reports one.</summary>
    public bool ShowBluetooth { get; set => Set(ref field, value); } = true;

    /// <summary>A small preview after a screenshot (Win+Shift+S, Snipping Tool), to open or save it.</summary>
    public bool ShowScreenshots { get; set => Set(ref field, value); } = true;

    /// <summary>A call app (Discord, Teams, Zoom…) using the microphone: the compact island shows the call and its length.</summary>
    public bool ShowCalls { get; set => Set(ref field, value); } = true;

    /// <summary>Windows notifications (messages etc.) repeated on the island. Off by default: they may be private.</summary>
    public bool ShowNotifications { get; set => Set(ref field, value); }

    // ---- Behaviour ----

    /// <summary>"tr" or "en"; null follows Windows' display language.</summary>
    public string? Language { get; set => Set(ref field, value); }

    public bool HideInFullscreen { get; set => Set(ref field, value); } = true;

    /// <summary>
    /// When a maximised window reaches up under the closed island: true shrinks it to a thin line,
    /// false (the default) keeps the compact player (and timer) on screen.
    /// </summary>
    public bool YieldToTopWindows { get; set => Set(ref field, value); }

    public bool WheelControlsVolume { get; set => Set(ref field, value); } = true;

    /// <summary>How much one wheel notch changes the volume: 1, 2 or 5 percent.</summary>
    public int WheelStepPercent { get; set => Set(ref field, Math.Clamp(value, 1, 10)); } = 2;

    public bool TimerSound { get; set => Set(ref field, value); } = true;
    public int HoverDelayMs { get; set => Set(ref field, Math.Clamp(value, 0, 1000)); } = 220;

    /// <summary>
    /// System-wide shortcut that mutes/unmutes the microphone; empty for none. K like Windows' own
    /// Win+Alt+K (which Windows keeps for itself); unlike AltGr+M on the Turkish F layout, AltGr+K types nothing.
    /// </summary>
    public string MicMuteHotkey { get; set => Set(ref field, value ?? ""); } = "Ctrl+Alt+K";

    // ---- Player and quick buttons ----

    /// <summary>How far the player's back / ahead buttons jump: 5, 10, 15 or 30 seconds; 0 hides them.</summary>
    public int SkipSeconds { get; set => Set(ref field, value is 0 or 5 or 10 or 15 or 30 ? value : 10); } = 10;

    /// <summary>Which quick buttons the open island shows: timer / stopwatch, sound devices, microphone.</summary>
    public bool ShowTimerButton { get; set => Set(ref field, value); } = true;
    public bool ShowSoundButton { get; set => Set(ref field, value); } = true;
    public bool ShowMicButton { get; set => Set(ref field, value); } = true;

    // ---- Placement and looks ----

    /// <summary>The island's size: 0.9, 1 (normal), 1.1 or 1.25.</summary>
    public double IslandScale { get; set => Set(ref field, value is 0.9 or 1.0 or 1.1 or 1.25 ? value : 1.0); } = 1.0;

    /// <summary>The monitor the island sits on (its device name, e.g. \\.\DISPLAY2); null for the primary one.</summary>
    public string? IslandMonitor { get; set => Set(ref field, value); }

    /// <summary>Where along that monitor's top edge the island's centre is, as a fraction of its width.</summary>
    public double IslandPosition { get; set => Set(ref field, Math.Clamp(value, 0, 1)); } = 0.5;

    /// <summary>A GIF shown next to the player's controls (a copy kept with the settings); null for none.</summary>
    public string? GifPath { get; set => Set(ref field, value); }

    // ---- Always on display ----

    public bool AodEnabled { get; set => Set(ref field, value); }
    public AodTrigger AodTrigger { get; set => Set(ref field, value); } = AodTrigger.Idle;
    public int AodIdleMinutes { get; set => Set(ref field, Math.Clamp(value, 1, 60)); } = 5;

    /// <summary>Where it shows: null on the island's monitor, "*" on all of them, otherwise a device name.</summary>
    public string? AodScreen { get; set => Set(ref field, value); }

    public bool AodClock { get; set => Set(ref field, value); } = true;
    public bool AodDate { get; set => Set(ref field, value); } = true;
    public bool AodMedia { get; set => Set(ref field, value); } = true;
    public bool AodBattery { get; set => Set(ref field, value); } = true;
    public bool AodTimer { get; set => Set(ref field, value); } = true;
    public bool AodPrivacy { get; set => Set(ref field, value); } = true;

    // ---- Shortcuts ----

    public const int MaxShortcuts = 8;

    /// <summary>The user's launch buttons in the open island, in order. Replaced as a whole on every change.</summary>
    public IReadOnlyList<ShortcutItem> Shortcuts { get; set => Set(ref field, value ?? []); } = [];

    // ---- Updates and first run ----

    public bool AutoUpdate { get; set => Set(ref field, value); } = true;

    /// <summary>The welcome guide has been seen (shown once, after the first install).</summary>
    public bool WelcomeShown { get; set => Set(ref field, value); }

    /// <summary>The version that ran last, to tell the user after an update.</summary>
    public string? LastRunVersion { get; set => Set(ref field, value); }

    /// <summary>True when the CPU temperature is actually on screen somewhere.</summary>
    [JsonIgnore]
    public bool WantsCpuTemperature => ShowStats && ShowCpu && ShowCpuTemperature;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every preference back to its default; the language, shortcuts, GIF and first-run state stay.</summary>
    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanWrite || property.Name is nameof(Language) or nameof(Shortcuts) or nameof(GifPath)
                or nameof(WelcomeShown) or nameof(LastRunVersion)) continue;
            property.SetValue(this, property.GetValue(defaults));
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
