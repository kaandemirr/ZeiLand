using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace ZeiLand.Services;

internal enum CpuTemperatureStatus
{
    NotStarted,
    NeedsDriver,
    NeedsAdmin,
    Starting,
    Available,
    Unsupported,
}

/// <summary>
/// CPU package temperature (e.g. Ryzen Tctl/Tdie, Intel Package) through LibreHardwareMonitor and the
/// PawnIO kernel driver. Windows has no unprivileged API for this, so it needs the driver plus admin mode.
/// Only the CPU sensor group is opened, and only once the temperature is first needed.
/// </summary>
internal sealed class CpuTemperatureReader : IDisposable
{
    // Most meaningful first: AMD reports Tctl/Tdie, Intel "CPU Package".
    private static readonly string[] PreferredSensors = ["Core (Tctl/Tdie)", "CPU Package", "Tctl", "Tdie", "Package"];

    private Computer? _computer;
    private IHardware? _cpu;
    private ISensor? _sensor;
    private Task? _opening;
    private volatile CpuTemperatureStatus _status;

    public CpuTemperatureStatus Status => _status;

    public static bool IsDriverInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key is not null;
        }
    }

    /// <summary>Opens the sensor in the background if the driver and admin mode are available. Cheap to repeat.</summary>
    public void EnsureStarted()
    {
        if (_opening is not null) return;
        if (!IsDriverInstalled)
        {
            _status = CpuTemperatureStatus.NeedsDriver;
            return;
        }
        if (!Elevation.IsElevated)
        {
            _status = CpuTemperatureStatus.NeedsAdmin;
            return;
        }

        _status = CpuTemperatureStatus.Starting;
        _opening = Task.Run(Open);
    }

    /// <summary>Latest temperature in °C, or null while unavailable.</summary>
    public double? Read()
    {
        if (_status != CpuTemperatureStatus.Available || _cpu is null || _sensor is null) return null;
        try
        {
            _cpu.Update();
        }
        catch (Exception)
        {
            return null;
        }
        return _sensor.Value is float value && value > 0 ? value : null;
    }

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception)
        {
        }
        _computer = null;
    }

    private void Open()
    {
        try
        {
            var computer = new Computer { IsCpuEnabled = true };
            computer.Open();
            var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            cpu?.Update();
            var sensor = cpu is null ? null : PickSensor(cpu.Sensors);
            if (sensor is null)
            {
                computer.Close();
                _status = CpuTemperatureStatus.Unsupported;
                return;
            }

            _computer = computer;
            _cpu = cpu;
            _sensor = sensor;
            _status = CpuTemperatureStatus.Available;
        }
        catch (Exception)
        {
            _status = CpuTemperatureStatus.Unsupported;
        }
    }

    private static ISensor? PickSensor(ISensor[] sensors)
    {
        var temperatures = sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
        foreach (var name in PreferredSensors)
        {
            var match = temperatures.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return temperatures.FirstOrDefault();
    }
}
