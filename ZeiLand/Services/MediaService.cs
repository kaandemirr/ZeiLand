using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace ZeiLand.Services;

/// <summary>
/// Follows the system "now playing" session (Spotify, browsers, media players) through
/// Global System Media Transport Controls. Purely event driven: nothing runs until a player reports a change.
/// </summary>
internal sealed class MediaService(Dispatcher dispatcher) : IDisposable
{
    private const int ArtworkDecodeSize = 160;

    private MediaManager? _manager;
    private MediaSession? _session;
    private (TimeSpan Target, long At)? _lastSkip;
    private int _generation;
    private IReadOnlyCollection<string> _hiddenApps = [];

    public event Action? Changed;

    /// <summary>Players came or went (for the settings window's list of sources).</summary>
    public event Action? SessionsChanged;

    /// <summary>
    /// Players (app ids) whose media the island ignores; while one of them is the system's current player,
    /// another one's is shown, a playing one first.
    /// </summary>
    public IReadOnlyCollection<string> HiddenApps
    {
        get => _hiddenApps;
        set
        {
            _hiddenApps = value;
            if (_manager is not null) AttachCurrentSession();
        }
    }

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public BitmapSource? Artwork { get; private set; }
    public Color Accent { get; private set; } = Colors.White;
    public bool IsPlaying { get; private set; }
    public bool HasMedia => _session is not null && Title.Length > 0;

    /// <summary>The player's app id, e.g. "Spotify.exe" or "MSEdge"; null without a session.</summary>
    public string? SourceAppId { get; private set; }

    /// <summary>The player accepts seeking (some browsers' sites and radio streams don't).</summary>
    public bool CanSeek { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await MediaManager.RequestAsync();
        }
        catch (Exception)
        {
            return; // Media integration is unavailable; the rest of the island still works.
        }

        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        AttachCurrentSession();
    }

    /// <summary>The app ids of the players that have a session right now.</summary>
    public IReadOnlyList<string> GetSessionApps()
    {
        try
        {
            return _manager?.GetSessions().Select(s => s.SourceAppUserModelId).Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>The app ids of the players playing right now, hidden ones too.</summary>
    public IReadOnlyList<string> GetPlayingApps()
    {
        try
        {
            return _manager?.GetSessions()
                .Where(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                .Select(s => s.SourceAppUserModelId).Where(id => !string.IsNullOrEmpty(id)).ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public Task TogglePlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync());
    public Task SkipNextAsync() => SendAsync(s => s.TrySkipNextAsync());
    public Task SkipPreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync());

    /// <summary>Jumps to <paramref name="position"/> from the start of the track.</summary>
    public Task SeekAsync(TimeSpan position) => SendAsync(s =>
        s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().StartTime + position).Ticks));

    /// <summary>Jumps <paramref name="offset"/> from where it is now (10 seconds back or ahead), within the track.</summary>
    public Task SkipByAsync(TimeSpan offset)
    {
        if (GetTimeline() is not { } timeline) return Task.CompletedTask;
        // Quick repeated clicks add up, before the player has reported where the last one landed.
        var position = timeline.Position;
        if (_lastSkip is { } last && Environment.TickCount64 - last.At < 1500)
            position = last.Target + (IsPlaying ? TimeSpan.FromMilliseconds(Environment.TickCount64 - last.At) : TimeSpan.Zero);
        var target = TimeSpan.FromTicks(Math.Clamp((position + offset).Ticks, 0, timeline.Duration.Ticks));
        _lastSkip = (target, Environment.TickCount64);
        return SeekAsync(target);
    }

    /// <summary>Current position and duration, or null when the player doesn't report a timeline.</summary>
    public (TimeSpan Position, TimeSpan Duration)? GetTimeline()
    {
        if (_session is not { } session) return null;
        try
        {
            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero) return null;

            var position = timeline.Position - timeline.StartTime;
            // Players only report the position when it changes; extrapolate while playing.
            var sinceUpdate = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (IsPlaying && sinceUpdate > TimeSpan.Zero && sinceUpdate < TimeSpan.FromHours(12))
                position += sinceUpdate;

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > duration) position = duration;
            return (position, duration);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
        DetachSession();
    }

    private void OnCurrentSessionChanged(MediaManager sender, CurrentSessionChangedEventArgs args) =>
        dispatcher.InvokeAsync(AttachCurrentSession);

    private void OnSessionsChanged(MediaManager sender, SessionsChangedEventArgs args) =>
        dispatcher.InvokeAsync(() =>
        {
            // With hidden players, the one to show may have just appeared or gone.
            if (_hiddenApps.Count > 0) AttachCurrentSession();
            SessionsChanged?.Invoke();
        });

    private void OnMediaPropertiesChanged(MediaSession sender, MediaPropertiesChangedEventArgs args) =>
        dispatcher.InvokeAsync(() => _ = RefreshPropertiesAsync());

    private void OnPlaybackInfoChanged(MediaSession sender, PlaybackInfoChangedEventArgs args) =>
        dispatcher.InvokeAsync(() =>
        {
            if (RefreshPlayback()) Changed?.Invoke();
        });

    private void AttachCurrentSession()
    {
        DetachSession();
        try
        {
            _session = ChooseSession();
        }
        catch (Exception)
        {
            _session = null;
        }

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
        try
        {
            SourceAppId = _session?.SourceAppUserModelId;
        }
        catch (Exception)
        {
            SourceAppId = null;
        }

        RefreshPlayback();
        _ = RefreshPropertiesAsync();
    }

    /// <summary>The system's current player, unless it's hidden: then another one, a playing one first.</summary>
    private MediaSession? ChooseSession()
    {
        var current = _manager?.GetCurrentSession();
        if (_manager is null || _hiddenApps.Count == 0 || (current is not null && !IsHidden(current))) return current;
        var others = _manager.GetSessions().Where(s => !IsHidden(s)).ToList();
        return others.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? others.FirstOrDefault();
    }

    private bool IsHidden(MediaSession session) => _hiddenApps.Contains(session.SourceAppUserModelId, StringComparer.OrdinalIgnoreCase);

    private void DetachSession()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session = null;
    }

    private bool RefreshPlayback()
    {
        bool playing, canSeek;
        try
        {
            var info = _session?.GetPlaybackInfo();
            playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            canSeek = info?.Controls.IsPlaybackPositionEnabled == true;
        }
        catch (Exception)
        {
            playing = canSeek = false;
        }

        if (playing == IsPlaying && canSeek == CanSeek) return false;
        IsPlaying = playing;
        CanSeek = canSeek;
        return true;
    }

    private async Task RefreshPropertiesAsync()
    {
        int generation = ++_generation;
        var session = _session;

        GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
        if (session is not null)
        {
            try
            {
                properties = await session.TryGetMediaPropertiesAsync();
            }
            catch (Exception)
            {
                // The player went away while we were asking.
            }
        }
        if (generation != _generation) return;

        string title = properties?.Title ?? "";
        string artist = properties?.Artist ?? "";
        if (title != Title || artist != Artist)
        {
            Artwork = null;
            Accent = Colors.White;
        }
        Title = title;
        Artist = artist;
        Changed?.Invoke();

        var artwork = await LoadArtworkAsync(properties?.Thumbnail);
        if (generation != _generation || artwork is null) return;

        Artwork = artwork.Value.Image;
        Accent = artwork.Value.Accent;
        Changed?.Invoke();
    }

    private async Task SendAsync(Func<MediaSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (_session is not { } session) return;
        try
        {
            await command(session);
        }
        catch (Exception)
        {
            // The player may have closed in the meantime.
        }
    }

    private static async Task<(BitmapSource Image, Color Accent)?> LoadArtworkAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;
        try
        {
            var buffer = new MemoryStream();
            using (var stream = await reference.OpenReadAsync())
            using (var input = stream.AsStreamForRead())
            {
                await input.CopyToAsync(buffer);
            }
            buffer.Position = 0;
            return await Task.Run(() => Decode(buffer));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (BitmapSource Image, Color Accent)? Decode(Stream data)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ArtworkDecodeSize;
            image.StreamSource = data;
            image.EndInit();
            image.Freeze();
            return (image, ComputeAccent(image));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Picks a vivid average colour from the artwork, lifted so it stays readable on black.</summary>
    private static Color ComputeAccent(BitmapSource image)
    {
        var scaled = new TransformedBitmap(image, new ScaleTransform(24.0 / image.PixelWidth, 24.0 / image.PixelHeight));
        var pixels32 = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int stride = pixels32.PixelWidth * 4;
        var pixels = new byte[stride * pixels32.PixelHeight];
        pixels32.CopyPixels(pixels, stride, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
            double max = Math.Max(pr, Math.Max(pg, pb));
            double min = Math.Min(pr, Math.Min(pg, pb));
            double saturation = max == 0 ? 0 : (max - min) / max;
            double weight = 0.05 + saturation * saturation * (max / 255);
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }

        r /= total;
        g /= total;
        b /= total;
        double peak = Math.Max(r, Math.Max(g, b));
        if (peak < 1) return Colors.White;

        double lift = peak < 230 ? 230 / peak : 1;
        return Color.FromRgb(ToByte(r * lift), ToByte(g * lift), ToByte(b * lift));

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
