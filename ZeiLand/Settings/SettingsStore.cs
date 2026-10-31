using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ZeiLand.Settings;

/// <summary>Loads <see cref="AppSettings"/> from %LOCALAPPDATA%\ZeiLand\settings.json and saves it shortly after each change.</summary>
internal sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeiLand", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly DispatcherTimer _saveTimer;

    public SettingsStore()
    {
        Current = Load();
        // Debounced: toggling several switches in a row writes the file once.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => Flush();
        Current.PropertyChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
    }

    public AppSettings Current { get; }

    /// <summary>Writes pending changes now (called on exit).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken file shouldn't stop the island; fall back to defaults.
        }
        return new AppSettings();
    }
}
