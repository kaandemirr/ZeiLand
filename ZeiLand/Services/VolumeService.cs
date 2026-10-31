using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ZeiLand.Native;

namespace ZeiLand.Services;

/// <summary>
/// Master volume and output level of the default output device. Windows pushes volume changes to us
/// through Core Audio callbacks; the level is only read on demand (by the level bars while visible).
/// </summary>
internal sealed class VolumeService : IDisposable
{
    private const int ClsCtxAll = 0x17;

    private readonly Dispatcher _dispatcher;
    private readonly EndpointCallback _endpointCallback;
    private readonly DeviceCallback _deviceCallback;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private static readonly TimeSpan AppMeterRefresh = TimeSpan.FromSeconds(2);

    private IMMDevice? _device;
    private IAudioMeterInformation? _meter;
    private float[] _channelPeaks = new float[2];
    private string? _meterApp;
    private readonly List<IAudioMeterInformation> _appMeters = [];
    private long _appMetersFoundAt;

    public VolumeService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _endpointCallback = new EndpointCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    public event Action? Changed;

    /// <summary>Another output device became the default (from the island, Windows or a headset connecting).</summary>
    public event Action? OutputChanged;

    public float Level { get; private set; }
    public bool IsMuted { get; private set; }

    public void Initialize()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
        }
        catch (COMException)
        {
            _enumerator = null;
            return;
        }
        AttachDefaultDevice();
    }

    /// <summary>
    /// The app whose sound the level bars follow (a media session's app id, e.g. "Spotify.exe"), so a
    /// voice chat or a game doesn't move them. Null, or an app without an audio session of its own:
    /// everything that plays.
    /// </summary>
    public void SetMeterApp(string? appId)
    {
        if (appId == _meterApp) return;
        _meterApp = appId;
        ReleaseAppMeters();
    }

    /// <summary>
    /// Peak sample level (0..1) of the left and right channels since the previous call — what the volume
    /// mixer's meters show — for the meter app if it has an audio session, otherwise for the whole output.
    /// False if there is no output device.
    /// </summary>
    public bool ReadPeaks(float[] peaks)
    {
        if (_meterApp is not null)
        {
            // Players open and close audio sessions as they go (a browser per page), so look again now and then.
            if (Stopwatch.GetElapsedTime(_appMetersFoundAt) > AppMeterRefresh) FindAppMeters();
            if (_appMeters.Count > 0)
            {
                peaks[0] = peaks[1] = 0;
                foreach (var meter in _appMeters)
                {
                    if (!TryRead(meter, out float left, out float right)) continue;
                    peaks[0] = Math.Max(peaks[0], left);
                    peaks[1] = Math.Max(peaks[1], right);
                }
                return true;
            }
        }

        if (_meter is null || !TryRead(_meter, out float outputLeft, out float outputRight)) return false;
        peaks[0] = outputLeft;
        peaks[1] = outputRight;
        return true;
    }

    private bool TryRead(IAudioMeterInformation meter, out float left, out float right)
    {
        left = right = 0;
        try
        {
            meter.GetMeteringChannelCount(out uint channels);
            if (channels >= 2)
            {
                // The call wants every channel; the first two are front left and right.
                if (_channelPeaks.Length < channels) _channelPeaks = new float[channels];
                meter.GetChannelsPeakValues(channels, _channelPeaks);
                left = _channelPeaks[0];
                right = _channelPeaks[1];
            }
            else
            {
                meter.GetPeakValue(out left);
                right = left;
            }
            return true;
        }
        catch (COMException)
        {
            return false; // The session or device went away; the next search drops it.
        }
    }

    private void FindAppMeters()
    {
        ReleaseAppMeters();
        _appMetersFoundAt = Stopwatch.GetTimestamp();
        if (_device is null || _meterApp is null) return;

        object? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            _device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out manager);
            ((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions);
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                session.GetProcessId(out uint processId);
                // An audio session object is also its own meter.
                if (processId != 0 && IsApp((int)processId, _meterApp)) _appMeters.Add((IAudioMeterInformation)session);
                else Marshal.ReleaseComObject(session);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
        }
        finally
        {
            if (sessions is not null) Marshal.ReleaseComObject(sessions);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }

    /// <summary>
    /// Whether a process belongs to a media app id: a Store app's package family ("Family!App"), an
    /// executable name ("Spotify.exe"), or a desktop app id named after its executable ("MSEdge", "Brave").
    /// </summary>
    private static bool IsApp(int processId, string appId)
    {
        int bang = appId.IndexOf('!');
        if (bang > 0)
            return string.Equals(WindowNative.GetPackageFamily(processId), appId[..bang], StringComparison.OrdinalIgnoreCase);

        string name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        var wanted = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId[..^4] : appId;
        return name.Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private void ReleaseAppMeters()
    {
        foreach (var meter in _appMeters) Marshal.ReleaseComObject(meter);
        _appMeters.Clear();
        _appMetersFoundAt = 0;
    }

    /// <summary>Changes the volume by <paramref name="delta"/> (0..1 scale), snapped to whole percents.</summary>
    public void Nudge(float delta)
    {
        if (_endpoint is null) return;
        try
        {
            var context = Guid.Empty;
            _endpoint.GetMasterVolumeLevelScalar(out float current);
            float next = Math.Clamp(MathF.Round((current + delta) * 100) / 100, 0f, 1f);
            if (IsMuted && delta > 0) _endpoint.SetMute(false, ref context);
            _endpoint.SetMasterVolumeLevelScalar(next, ref context);
        }
        catch (COMException)
        {
        }
    }

    public void Dispose()
    {
        DetachEndpoint();
        if (_enumerator is null) return;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
        }
        catch (COMException)
        {
        }
        Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    private void AttachDefaultDevice()
    {
        DetachEndpoint();
        if (_enumerator is null) return;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out _device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            _device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object instance);

            _endpoint = (IAudioEndpointVolume)instance;
            _endpoint.RegisterControlChangeNotify(_endpointCallback);

            var meterIid = typeof(IAudioMeterInformation).GUID;
            _device.Activate(ref meterIid, ClsCtxAll, IntPtr.Zero, out object meter);
            _meter = (IAudioMeterInformation)meter;

            _endpoint.GetMasterVolumeLevelScalar(out float level);
            _endpoint.GetMute(out bool muted);
            Level = level;
            IsMuted = muted;
        }
        catch (COMException)
        {
            DetachEndpoint(); // No output device right now.
        }
    }

    private void DetachEndpoint()
    {
        ReleaseAppMeters();
        if (_meter is not null) Marshal.ReleaseComObject(_meter);
        _meter = null;
        if (_device is not null) Marshal.ReleaseComObject(_device);
        _device = null;
        if (_endpoint is null) return;
        try
        {
            _endpoint.UnregisterControlChangeNotify(_endpointCallback);
        }
        catch (COMException)
        {
        }
        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    private void Update(float level, bool muted)
    {
        Level = level;
        IsMuted = muted;
        Changed?.Invoke();
    }

    // Core Audio calls these on its own worker threads; hop to the UI thread before touching state.

    private sealed class EndpointCallback(VolumeService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
            owner._dispatcher.InvokeAsync(() => owner.Update(data.MasterVolume, data.Muted != 0));
            return 0;
        }
    }

    private sealed class DeviceCallback(VolumeService owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia)
            {
                owner._dispatcher.InvokeAsync(() =>
                {
                    owner.AttachDefaultDevice();
                    owner.OutputChanged?.Invoke();
                });
            }
            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, int newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }
}
