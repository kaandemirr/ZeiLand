using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using ZeiLand.Settings;

namespace ZeiLand.Services;

internal enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    Ready,
    Installing,
    Failed,
}

/// <summary>
/// Keeps ZeiLand current from the GitHub releases of <see cref="Repository"/>: a look shortly after start and
/// then every few hours (one small request), a newer ZEIGARN setup is downloaded and, at a quiet moment,
/// run silently. The setup closes the island, keeps the earlier choices (start with Windows) and starts the
/// new version. Nothing else is sent anywhere.
/// </summary>
internal sealed class UpdateService
{
    public const string Repository = "zeigarn/ZeiLand";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan InstallRetry = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly Func<bool> _isQuietMoment;
    private readonly Action _prepareToExit;
    private readonly DispatcherTimer _timer = new();
    private string? _installer;

    /// <param name="isQuietMoment">True when restarting the island now won't interrupt anything.</param>
    /// <param name="prepareToExit">Saves state before the setup closes the island.</param>
    public UpdateService(AppSettings settings, Func<bool> isQuietMoment, Action prepareToExit)
    {
        _settings = settings;
        _isQuietMoment = isQuietMoment;
        _prepareToExit = prepareToExit;
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            if (State == UpdateState.Ready) TryInstall();
            else await CheckAsync(installNow: false);
            Schedule();
        };
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.AutoUpdate)) Schedule();
        };
    }

    /// <summary>State, version or error changed (for the settings window).</summary>
    public event Action? Changed;

    /// <summary>The setup is about to run; the island says so.</summary>
    public event Action<string>? Installing;

    public static string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public UpdateState State { get; private set; }
    public string? LatestVersion { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastChecked { get; private set; }

    public void Start()
    {
        _timer.Interval = FirstCheck; // not during sign-in, when everything else starts too
        Schedule();
    }

    private void Schedule()
    {
        if (!_settings.AutoUpdate)
        {
            _timer.Stop();
            return;
        }
        if (!_timer.IsEnabled)
        {
            if (State == UpdateState.Ready) _timer.Interval = InstallRetry;
            else if (LastChecked is not null) _timer.Interval = CheckInterval;
            _timer.Start();
        }
    }

    /// <summary>Looks for a newer release and downloads it. Installs it right away if asked, or if updates are automatic.</summary>
    public async Task CheckAsync(bool installNow)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return;
        if (State == UpdateState.Ready && _installer is not null)
        {
            if (installNow) TryInstall(force: true);
            return;
        }

        SetState(UpdateState.Checking);
        LastChecked = DateTime.Now; // also after a failure: the next try waits the full interval
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ZeiLand/{CurrentVersion}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                SetState(UpdateState.UpToDate); // nothing published yet
                return;
            }
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            var tag = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest) || latest <= Version.Parse(CurrentVersion))
            {
                SetState(UpdateState.UpToDate);
                return;
            }
            var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
                a.GetProperty("name").GetString() is { } name
                && name.StartsWith("ZEIGARN-Setup-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind != JsonValueKind.Object)
            {
                SetState(UpdateState.UpToDate); // a release without a setup (yet)
                return;
            }

            LatestVersion = latest.ToString(3);
            SetState(UpdateState.Downloading);
            _installer = await DownloadAsync(http, asset);
            SetState(UpdateState.Ready);
            if (installNow || _settings.AutoUpdate) TryInstall(force: installNow);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException
            or JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException
            or UnauthorizedAccessException)
        {
            SetState(UpdateState.Failed, e is InvalidDataException ? e.Message : Loc.T("GitHubUnreachable"));
        }
    }

    private static async Task<string> DownloadAsync(HttpClient http, JsonElement asset)
    {
        var name = Path.GetFileName(asset.GetProperty("name").GetString()!);
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var folder = Path.Combine(Path.GetTempPath(), "ZeiLand-update");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);

        await using (var source = await http.GetStreamAsync(url))
        await using (var file = File.Create(path))
        {
            await source.CopyToAsync(file);
        }

        // GitHub publishes a SHA-256 for every release file; a damaged download is never run.
        if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } expected
            && expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            await using var file = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!actual.Equals(expected["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException(Loc.T("DownloadDamaged"));
            }
        }
        return path;
    }

    /// <summary>Runs the downloaded setup, unless the island is busy (then later). <paramref name="force"/>: the user asked.</summary>
    private void TryInstall(bool force = false)
    {
        if (_installer is null || !File.Exists(_installer))
        {
            _installer = null;
            SetState(UpdateState.Idle);
            return;
        }
        if (!force && !_isQuietMoment())
        {
            Schedule(); // a full-screen game, a running timer, the pointer on the island: try again later
            return;
        }

        SetState(UpdateState.Installing);
        Installing?.Invoke(LatestVersion ?? "");
        var installer = _installer;
        // A moment for the island to show the message; then the setup closes it.
        var start = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        start.Tick += (_, _) =>
        {
            start.Stop();
            _prepareToExit();
            try
            {
                // Elevated like the island (logon task): no prompt. Otherwise Windows asks once.
                Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
                {
                    UseShellExecute = true,
                })?.Dispose();
            }
            catch (Win32Exception)
            {
                SetState(UpdateState.Ready, Loc.T("InstallDenied")); // stays downloaded; "Yükle" tries again
            }
        };
        start.Start();
    }

    private void SetState(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }
}
