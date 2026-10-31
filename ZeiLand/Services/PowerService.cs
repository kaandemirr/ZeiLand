using System.Runtime.InteropServices;
using System.Windows.Interop;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand.Services;

internal readonly record struct PowerStatus(bool HasBattery, bool OnAc, int Percent);

/// <summary>
/// Charger plug/unplug and low-battery events, delivered by Windows as power-setting
/// notifications to the hidden host window.
/// </summary>
internal sealed class PowerService : IDisposable
{
    private static readonly Guid PowerSourceSetting = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private static readonly Guid BatteryPercentSetting = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");

    private readonly HwndSource _host;
    private readonly HwndSourceHook _hook;
    private readonly IntPtr _sourceRegistration;
    private readonly IntPtr _percentRegistration;
    private bool _sourcePrimed;
    private int _lastPercent = -1;

    public PowerService(HwndSource host)
    {
        _host = host;
        _hook = WndProc;
        host.AddHook(_hook);

        var source = PowerSourceSetting;
        var percent = BatteryPercentSetting;
        _sourceRegistration = RegisterPowerSettingNotification(host.Handle, ref source, 0);
        _percentRegistration = RegisterPowerSettingNotification(host.Handle, ref percent, 0);
    }

    public event Action? PowerSourceChanged;
    public event Action? BatteryLow;

    public static PowerStatus GetStatus()
    {
        if (!GetSystemPowerStatus(out var status)) return default;
        bool hasBattery = status.BatteryFlag != 255 && (status.BatteryFlag & 128) == 0;
        int percent = status.BatteryLifePercent > 100 ? 0 : status.BatteryLifePercent;
        return new PowerStatus(hasBattery, status.ACLineStatus == 1, percent);
    }

    public void Dispose()
    {
        _host.RemoveHook(_hook);
        if (_sourceRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(_sourceRegistration);
        if (_percentRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(_percentRegistration);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_POWERBROADCAST || (int)wParam != PBT_POWERSETTINGCHANGE) return IntPtr.Zero;

        // POWERBROADCAST_SETTING: GUID (16 bytes), DWORD length, then the data.
        var setting = Marshal.PtrToStructure<Guid>(lParam);
        int value = Marshal.ReadInt32(lParam, 20);

        if (setting == PowerSourceSetting)
        {
            // Registering immediately reports the current state; only react to real changes.
            if (_sourcePrimed) PowerSourceChanged?.Invoke();
            _sourcePrimed = true;
        }
        else if (setting == BatteryPercentSetting)
        {
            bool crossed = _lastPercent > 0 && (Crossed(20) || Crossed(10));
            _lastPercent = value;
            if (crossed && !GetStatus().OnAc) BatteryLow?.Invoke();
        }

        return IntPtr.Zero;

        bool Crossed(int threshold) => _lastPercent > threshold && value <= threshold;
    }
}
