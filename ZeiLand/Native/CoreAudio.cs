using System.Runtime.InteropServices;

namespace ZeiLand.Native;

// Minimal Core Audio (MMDevice API) definitions — just enough to read the master volume and output
// level, be notified when the volume or the default output device changes, and see (and hear about)
// which processes are recording.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public int Muted;
    public float MasterVolume;
    public uint Channels;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator
{
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(int access, out IPropertyStore properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
}

// Only the string and 32-bit integer cases are read. 24 bytes, as on x64.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] public uint UInt32;

    public const ushort VT_UI4 = 19, VT_LPWSTR = 31, VT_CLSID = 72;

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);
}

// Undocumented but long-stable (Windows 7+) interface the Sound control panel uses to change the default
// device. Only SetDefaultEndpoint is called; the slots before it just keep the vtable order.
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    void GetMixFormat();
    void GetDeviceFormat();
    void ResetDeviceFormat();
    void SetDeviceFormat();
    void GetProcessingPeriod();
    void SetProcessingPeriod();
    void GetShareMode();
    void SetShareMode();
    void GetPropertyValue();
    void SetPropertyValue();
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}

internal static class AudioSessionState
{
    public const int Active = 1;
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    void GetChannelCount(out uint count);
    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
    void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

// A new audio session appeared on a device (an app opened a stream).
[ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IntPtr newSession);
}

// Per-session events; only the state changes matter here, the rest just hold their vtable slots.
[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig] int OnDisplayNameChanged(IntPtr name, IntPtr eventContext);
    [PreserveSig] int OnIconPathChanged(IntPtr path, IntPtr eventContext);
    [PreserveSig] int OnSimpleVolumeChanged(float volume, int muted, IntPtr eventContext);
    [PreserveSig] int OnChannelVolumeChanged(uint channels, IntPtr volumes, uint changedChannel, IntPtr eventContext);
    [PreserveSig] int OnGroupingParamChanged(IntPtr groupingParam, IntPtr eventContext);
    [PreserveSig] int OnStateChanged(int state);
    [PreserveSig] int OnSessionDisconnected(int reason);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl2 session);
}

// IAudioSessionControl's methods followed by IAudioSessionControl2's; state, events and process id are used.
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    void GetState(out int state);
    void GetDisplayName(out IntPtr name);
    void SetDisplayName(IntPtr name, IntPtr eventContext);
    void GetIconPath(out IntPtr path);
    void SetIconPath(IntPtr path, IntPtr eventContext);
    void GetGroupingParam(out Guid groupingParam);
    void SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents notifications);
    void UnregisterAudioSessionNotification(IAudioSessionEvents notifications);
    void GetSessionIdentifier(out IntPtr id);
    void GetSessionInstanceIdentifier(out IntPtr id);
    void GetProcessId(out uint processId);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
    void GetMeteringChannelCount(out uint count);
    void GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
    void QueryHardwareSupport(out uint mask);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(IntPtr notifyData);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}
