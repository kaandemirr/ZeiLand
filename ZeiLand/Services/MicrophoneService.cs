using System.Runtime.InteropServices;
using System.Windows.Threading;
using ZeiLand.Native;

namespace ZeiLand.Services;

/// <summary>
/// Mute state of the default recording device — the same switch as Windows' own microphone mute and,
/// on many headsets, the mute button. Windows reports changes through Core Audio callbacks.
/// </summary>
internal sealed class MicrophoneService : IDisposable
{
    private const int ClsCtxAll = 0x17;

    private readonly Dispatcher _dispatcher;
    private readonly EndpointCallback _endpointCallback;
    private readonly DeviceCallback _deviceCallback;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private bool _reportedAvailable;

    public MicrophoneService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _endpointCallback = new EndpointCallback(this);
        _deviceCallback = new DeviceCallback(this);
    }

    /// <summary>
    /// The mute state changed (from here, a headset button or another app), or a microphone came or went.
    /// </summary>
    public event Action? Changed;

    public bool IsAvailable => _endpoint is not null;
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

    public void ToggleMute()
    {
        if (_endpoint is null) return;
        try
        {
            var context = Guid.Empty;
            _endpoint.SetMute(!IsMuted, ref context);
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
        bool muted = false;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Console, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object instance);
            Marshal.ReleaseComObject(device);

            _endpoint = (IAudioEndpointVolume)instance;
            _endpoint.RegisterControlChangeNotify(_endpointCallback);
            _endpoint.GetMute(out muted);
        }
        catch (COMException)
        {
            DetachEndpoint(); // No microphone right now.
        }
        // A different microphone (or none) may well be in a different state.
        Update(muted);
    }

    private void DetachEndpoint()
    {
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

    private void Update(bool muted)
    {
        // Volume-only changes also arrive here.
        if (muted == IsMuted && IsAvailable == _reportedAvailable) return;
        IsMuted = muted;
        _reportedAvailable = IsAvailable;
        Changed?.Invoke();
    }

    // Core Audio calls these on its own worker threads; hop to the UI thread before touching state.

    private sealed class EndpointCallback(MicrophoneService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
            owner._dispatcher.InvokeAsync(() => owner.Update(data.Muted != 0));
            return 0;
        }
    }

    private sealed class DeviceCallback(MicrophoneService owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Capture && role == ERole.Console)
                owner._dispatcher.InvokeAsync(owner.AttachDefaultDevice);
            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, int newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }
}
