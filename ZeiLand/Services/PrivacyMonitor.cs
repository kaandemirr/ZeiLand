using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ZeiLand.Native;

namespace ZeiLand.Services;

/// <param name="Exe">The executable's name without ".exe", e.g. "Discord" or "ms-teams".</param>
/// <param name="Name">What to show for it, e.g. "Microsoft Teams".</param>
internal sealed record AppUse(string Exe, string Name);

/// <summary>
/// Which apps are using the microphone or the camera right now. Microphone: processes with an active
/// recording stream in Core Audio (what the volume mixer sees). Camera: processes streaming from a camera,
/// as the camera frame server reports them through Media Foundation. Both push changes; nothing is polled.
/// </summary>
/// <remarks>
/// Earlier versions read the records behind the Windows privacy indicator (the CapabilityAccessManager
/// consent store), but newer Windows builds stopped updating them, so apps in a call went unnoticed.
/// </remarks>
internal sealed class PrivacyMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private MicrophoneWatch? _microphone;
    private CameraWatch? _camera;
    private IReadOnlyCollection<int> _microphoneIds = [];
    private IReadOnlyCollection<int> _cameraIds = [];

    public PrivacyMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event Action? Changed;

    public IReadOnlyList<string> MicrophoneApps { get; private set; } = [];
    public IReadOnlyList<string> CameraApps { get; private set; } = [];

    /// <summary>The apps using the microphone, with their executables (to tell a call app from the rest).</summary>
    public IReadOnlyList<AppUse> MicrophoneUsers { get; private set; } = [];

    /// <summary>Starts or stops watching; stopped, it costs nothing and reports no apps.</summary>
    public bool Enabled
    {
        get => _microphone is not null;
        set
        {
            if (value == Enabled) return;
            if (value)
            {
                lock (_lock) _microphoneIds = _cameraIds = [];
                _microphone = new MicrophoneWatch(this);
                _camera = CameraWatch.TryStart(this);
            }
            else
            {
                Stop();
                MicrophoneApps = CameraApps = [];
                MicrophoneUsers = [];
                Changed?.Invoke();
            }
        }
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        _microphone?.Dispose();
        _camera?.Dispose();
        _microphone = null;
        _camera = null;
    }

    /// <summary>A watcher's complete current set of process ids (called on worker threads).</summary>
    private void Report(IReadOnlyCollection<int> processIds, bool camera)
    {
        List<string> microphoneNames, cameraNames;
        List<AppUse> microphoneUsers;
        lock (_lock)
        {
            if (camera) _cameraIds = processIds;
            else _microphoneIds = processIds;
            microphoneNames = Names(_microphoneIds);
            cameraNames = Names(_cameraIds);
            microphoneUsers = Users(_microphoneIds);
        }
        _dispatcher.InvokeAsync(() =>
        {
            if (!Enabled) return;
            if (microphoneNames.SequenceEqual(MicrophoneApps) && cameraNames.SequenceEqual(CameraApps)
                && microphoneUsers.SequenceEqual(MicrophoneUsers)) return;
            MicrophoneApps = microphoneNames;
            CameraApps = cameraNames;
            MicrophoneUsers = microphoneUsers;
            Changed?.Invoke();
        });
    }

    private List<AppUse> Users(IReadOnlyCollection<int> processIds) => processIds
        .Where(id => id != 0 && id != Environment.ProcessId)
        .Select(id => new AppUse(WindowNative.GetProcessPath(id) is { } path ? Path.GetFileNameWithoutExtension(path) : "",
            DisplayName(id)))
        .Distinct()
        .ToList();

    private List<string> Names(IReadOnlyCollection<int> processIds) => processIds
        .Where(id => id != 0 && id != Environment.ProcessId)
        .Select(DisplayName)
        .Distinct()
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>"Discord", "Brave Browser": the executable's description, otherwise its name.</summary>
    private string DisplayName(int processId)
    {
        var path = WindowNative.GetProcessPath(processId);
        if (path is null)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.ProcessName;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                return "?"; // Exited meanwhile; the next report drops it.
            }
        }
        if (_names.TryGetValue(path, out var cached)) return cached;

        string? description = null;
        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
        }
        catch (FileNotFoundException)
        {
        }
        var name = string.IsNullOrEmpty(description) ? Path.GetFileNameWithoutExtension(path) : description;
        _names[path] = name;
        return name;
    }

    /// <summary>
    /// Active recording sessions on every recording device. Core Audio tells us when a session appears or
    /// changes state and when a device comes or goes; each time (debounced) the sessions are listed again.
    /// Runs on thread-pool threads: session notifications need the multithreaded apartment.
    /// </summary>
    private sealed class MicrophoneWatch : IMMNotificationClient, IAudioSessionNotification, IAudioSessionEvents, IDisposable
    {
        private const int DeviceStateActive = 1, ClsCtxAll = 0x17;

        private readonly PrivacyMonitor _owner;
        private readonly Lock _lock = new();
        private readonly Timer _rescan;
        private readonly List<IAudioSessionManager2> _managers = [];
        private readonly List<IAudioSessionControl2> _sessions = [];
        private IMMDeviceEnumerator? _enumerator;
        private bool _disposed;

        public MicrophoneWatch(PrivacyMonitor owner)
        {
            _owner = owner;
            _rescan = new Timer(_ => Rescan());
            _rescan.Change(0, Timeout.Infinite);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _rescan.Dispose();
                Release();
                if (_enumerator is null) return;
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch (COMException)
                {
                }
                Marshal.ReleaseComObject(_enumerator);
                _enumerator = null;
            }
        }

        private void Schedule()
        {
            try
            {
                _rescan.Change(250, Timeout.Infinite); // an app starting to record touches several sessions
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Rescan()
        {
            var recording = new HashSet<int>();
            lock (_lock)
            {
                if (_disposed) return;
                Release();
                try
                {
                    if (_enumerator is null)
                    {
                        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                        _enumerator.RegisterEndpointNotificationCallback(this);
                    }
                    _enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceStateActive, out var devices);
                    devices.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        devices.Item(i, out var device);
                        try
                        {
                            Watch(device, recording);
                        }
                        catch (Exception e) when (e is COMException or InvalidCastException)
                        {
                            // The device is going away; its removal is reported next.
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(device);
                        }
                    }
                    Marshal.ReleaseComObject(devices);
                }
                catch (COMException)
                {
                    // The audio service is restarting; device notifications follow.
                }
            }
            _owner.Report(recording, camera: false);
        }

        private void Watch(IMMDevice device, HashSet<int> recording)
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object instance);
            var manager = (IAudioSessionManager2)instance;
            _managers.Add(manager);
            // New sessions are only reported once a session enumerator exists.
            manager.GetSessionEnumerator(out var sessions);
            manager.RegisterSessionNotification(this);
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                _sessions.Add(session);
                session.RegisterAudioSessionNotification(this);
                session.GetState(out int state);
                session.GetProcessId(out uint processId);
                if (state == AudioSessionState.Active) recording.Add((int)processId);
            }
            Marshal.ReleaseComObject(sessions);
        }

        private void Release()
        {
            foreach (var session in _sessions)
            {
                try
                {
                    session.UnregisterAudioSessionNotification(this);
                }
                catch (COMException)
                {
                }
                Marshal.ReleaseComObject(session);
            }
            _sessions.Clear();
            foreach (var manager in _managers)
            {
                try
                {
                    manager.UnregisterSessionNotification(this);
                }
                catch (COMException)
                {
                }
                Marshal.ReleaseComObject(manager);
            }
            _managers.Clear();
        }

        // Core Audio calls these on its own threads. Recording starting or stopping, and devices coming or
        // going, warrant a fresh look; volume and name changes don't.

        public int OnSessionCreated(IntPtr newSession) => ScheduleAndReturn();
        public int OnStateChanged(int state) => ScheduleAndReturn();
        public int OnSessionDisconnected(int reason) => ScheduleAndReturn();
        public int OnDeviceStateChanged(string deviceId, int newState) => ScheduleAndReturn();
        public int OnDeviceAdded(string deviceId) => ScheduleAndReturn();
        public int OnDeviceRemoved(string deviceId) => ScheduleAndReturn();

        public int OnDisplayNameChanged(IntPtr name, IntPtr eventContext) => 0;
        public int OnIconPathChanged(IntPtr path, IntPtr eventContext) => 0;
        public int OnSimpleVolumeChanged(float volume, int muted, IntPtr eventContext) => 0;
        public int OnChannelVolumeChanged(uint channels, IntPtr volumes, uint changedChannel, IntPtr eventContext) => 0;
        public int OnGroupingParamChanged(IntPtr groupingParam, IntPtr eventContext) => 0;
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

        private int ScheduleAndReturn()
        {
            Schedule();
            return 0;
        }
    }

    /// <summary>Processes streaming from a camera, reported by the camera frame server whenever that changes.</summary>
    private sealed class CameraWatch : IMFSensorActivitiesReportCallback, IDisposable
    {
        private readonly PrivacyMonitor _owner;
        private readonly Lock _lock = new();
        private readonly Dictionary<string, List<int>> _streaming = []; // camera -> processes streaming from it
        private IMFSensorActivityMonitor? _monitor;
        private bool _mediaFoundation;

        private CameraWatch(PrivacyMonitor owner) => _owner = owner;

        /// <summary>Null where Windows has no sensor activity monitor (before 1703, or N editions without Media Foundation).</summary>
        public static CameraWatch? TryStart(PrivacyMonitor owner)
        {
            var watch = new CameraWatch(owner);
            try
            {
                watch._mediaFoundation = SensorActivity.MFStartup(SensorActivity.MF_VERSION, SensorActivity.MFSTARTUP_LITE) >= 0;
                if (watch._mediaFoundation && SensorActivity.MFCreateSensorActivityMonitor(watch, out var monitor) >= 0)
                {
                    watch._monitor = monitor;
                    monitor.Start();
                    return watch;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException)
            {
            }
            watch.Dispose();
            return null;
        }

        public void Dispose()
        {
            // Stopped outside the lock: Stop may wait for a report in progress, which takes the lock.
            IMFSensorActivityMonitor? monitor;
            lock (_lock)
            {
                monitor = _monitor;
                _monitor = null;
            }
            if (monitor is not null)
            {
                try
                {
                    monitor.Stop();
                }
                catch (COMException)
                {
                }
                Marshal.ReleaseComObject(monitor);
            }
            if (_mediaFoundation) SensorActivity.MFShutdown();
            _mediaFoundation = false;
        }

        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            HashSet<int> streaming;
            try
            {
                lock (_lock)
                {
                    if (_monitor is null) return 0;
                    report.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        report.GetActivityReport(i, out var camera);
                        try
                        {
                            _streaming[SymbolicLink(camera)] = StreamingProcesses(camera);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(camera);
                        }
                    }
                    streaming = _streaming.Values.SelectMany(ids => ids).ToHashSet();
                }
            }
            catch (Exception e) when (e is COMException or InvalidCastException)
            {
                return 0; // A camera unplugged mid-report; the next report has the new picture.
            }
            finally
            {
                Marshal.ReleaseComObject(report);
            }
            _owner.Report(streaming, camera: true);
            return 0;
        }

        private static string SymbolicLink(IMFSensorActivityReport camera)
        {
            var buffer = new char[1024];
            camera.GetSymbolicLink(buffer, (uint)buffer.Length, out uint written);
            return new string(buffer, 0, (int)Math.Min(written, (uint)buffer.Length)).TrimEnd('\0');
        }

        private static List<int> StreamingProcesses(IMFSensorActivityReport camera)
        {
            var ids = new List<int>();
            camera.GetProcessCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                camera.GetProcessActivity(i, out var activity);
                try
                {
                    activity.GetStreamingState(out bool streaming);
                    if (!streaming) continue;
                    activity.GetProcessId(out uint processId);
                    ids.Add((int)processId);
                }
                finally
                {
                    Marshal.ReleaseComObject(activity);
                }
            }
            return ids;
        }
    }
}
