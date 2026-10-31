using System.Runtime.InteropServices;
using ZeiLand.Native;

namespace ZeiLand.Services;

/// <param name="Name">The hardware, e.g. "CORSAIR VOID WIRELESS v2 Gaming Headset".</param>
/// <param name="Description">The endpoint, e.g. "Headphones" or "Speakers".</param>
internal sealed record AudioDevice(string Id, string Name, string Description, bool IsHeadphones, bool IsDefault);

/// <summary>Output and input devices, and switching the default one — what the Sound settings page does.</summary>
internal static class AudioDevices
{
    private const int DeviceStateActive = 1;
    private const int StgmRead = 0;

    private static readonly PropertyKey FriendlyName = Key("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    private static readonly PropertyKey DeviceDescription = Key("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    private static readonly PropertyKey InterfaceName = Key("026e516e-b814-414b-83cd-856d6fef4822", 2);
    private static readonly PropertyKey FormFactor = Key("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);

    public static List<AudioDevice> GetOutputs() => GetDevices(EDataFlow.Render, ERole.Multimedia);

    /// <summary>Microphones; the default one is what calls and the mute shortcut use.</summary>
    public static List<AudioDevice> GetInputs() => GetDevices(EDataFlow.Capture, ERole.Console);

    private static List<AudioDevice> GetDevices(EDataFlow flow, ERole defaultRole)
    {
        var devices = new List<AudioDevice>();
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                enumerator.GetDefaultAudioEndpoint(flow, defaultRole, out var current);
                current.GetId(out defaultId);
                Marshal.ReleaseComObject(current);
            }
            catch (COMException)
            {
                // No default device right now.
            }

            enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out var collection);
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                if (Describe(device, defaultId) is { } description) devices.Add(description);
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
        }
        catch (COMException)
        {
        }
        finally
        {
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
        return devices;
    }

    /// <summary>Makes a device the default for everything (media, games, calls). False if Windows refused.</summary>
    public static bool SetDefault(string deviceId)
    {
        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            bool done = true;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                done &= policy.SetDefaultEndpoint(deviceId, role) == 0;
            return done;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (policy is not null) Marshal.ReleaseComObject(policy);
        }
    }

    public static AudioDevice? Describe(IMMDevice device, string? defaultId)
    {
        try
        {
            device.GetId(out string id);
            device.OpenPropertyStore(StgmRead, out var properties);
            try
            {
                string? hardware = ReadString(properties, InterfaceName);
                string? endpoint = ReadString(properties, DeviceDescription);
                string name = hardware ?? ReadString(properties, FriendlyName) ?? endpoint ?? id;
                uint formFactor = ReadUInt32(properties, FormFactor) ?? 0;
                return new AudioDevice(id, name, endpoint ?? "", formFactor is 3 or 5, id == defaultId); // headphones, headset
            }
            finally
            {
                Marshal.ReleaseComObject(properties);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? ReadString(IPropertyStore properties, PropertyKey key)
    {
        properties.GetValue(ref key, out var value);
        try
        {
            return value.Type == PropVariant.VT_LPWSTR ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariant.PropVariantClear(ref value);
        }
    }

    private static uint? ReadUInt32(IPropertyStore properties, PropertyKey key)
    {
        properties.GetValue(ref key, out var value);
        try
        {
            return value.Type == PropVariant.VT_UI4 ? value.UInt32 : null;
        }
        finally
        {
            PropVariant.PropVariantClear(ref value);
        }
    }

    private static PropertyKey Key(string formatId, int propertyId) =>
        new() { FormatId = new Guid(formatId), PropertyId = propertyId };
}
