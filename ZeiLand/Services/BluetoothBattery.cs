using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace ZeiLand.Services;

/// <summary>
/// Whether an audio device is a Bluetooth one, and its battery level when it reports one: hands-free
/// headsets and speakers do, and Windows shows the same number under Settings → Bluetooth & devices.
/// One query of the device's container, only when a device connects or becomes the default.
/// </summary>
internal static class BluetoothBattery
{
    // DEVPKEY_Bluetooth_Battery: the percentage the device last reported over Bluetooth.
    private const string BatteryLevel = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static async Task<(bool IsBluetooth, int? Percent)> ReadAsync(Guid container)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{container}}}\"",
                [BatteryLevel], DeviceInformationKind.Device);
            bool bluetooth = false;
            int? percent = null;
            foreach (var device in devices)
            {
                if (device.Id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) bluetooth = true;
                if (device.Properties.TryGetValue(BatteryLevel, out var value) && value is IConvertible number)
                    percent = Math.Clamp(number.ToInt32(null), 0, 100);
            }
            return (bluetooth, percent);
        }
        catch (Exception e) when (e is COMException or ArgumentException or UnauthorizedAccessException)
        {
            return (false, null);
        }
    }
}
