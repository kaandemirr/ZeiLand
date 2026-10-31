using System.Runtime.InteropServices;
using static ZeiLand.Native.SystemInfoNative;

namespace ZeiLand.Services;

/// <summary>Usage values are 0..1; optional values are null when unavailable or not requested.</summary>
internal readonly record struct SystemStats(
    double CpuUsage,
    double? CpuTemperature,
    double RamUsage,
    double RamUsedGb,
    double? GpuUsage,
    double? GpuTemperature);

/// <summary>
/// CPU, RAM and GPU readings. Nothing runs in the background: the island calls
/// <see cref="Begin"/> when it expands and <see cref="Sample"/> once a second while expanded.
/// </summary>
internal sealed class SystemStatsService : IDisposable
{
    private const string GpuEngineCounter = @"\GPU Engine(*)\Utilization Percentage";

    private readonly Dictionary<string, double> _engineLoad = new();
    private long _lastIdle;
    private long _lastTotal;

    private bool _gpuInitialized;
    private uint _gpuAdapter;
    private string? _gpuLuid; // "luid_0x00000000_0x0000f2b7" — how PDH names this adapter's instances
    private IntPtr _pdhQuery;
    private IntPtr _pdhCounter;
    private IntPtr _pdhBuffer;
    private uint _pdhBufferSize;

    public CpuTemperatureReader CpuTemperature { get; } = new();

    /// <summary>GPU readings cost a performance-counter query; skip them when the GPU isn't shown.</summary>
    public bool IncludeGpu { get; set; } = true;

    public bool IncludeCpuTemperature { get; set; } = true;

    /// <summary>Starts a measurement window; the next <see cref="Sample"/> averages usage over it.</summary>
    public void Begin()
    {
        if (IncludeGpu) EnsureGpu();
        if (IncludeCpuTemperature) CpuTemperature.EnsureStarted();
        ReadCpuTimes(out _lastIdle, out _lastTotal);
        if (IncludeGpu && _pdhQuery != IntPtr.Zero) PdhCollectQueryData(_pdhQuery);
    }

    public SystemStats Sample()
    {
        ReadCpuTimes(out long idle, out long total);
        // Kernel time includes idle time, so busy = total - idle.
        double cpu = total > _lastTotal ? 1 - (double)(idle - _lastIdle) / (total - _lastTotal) : 0;
        _lastIdle = idle;
        _lastTotal = total;

        var (ram, ramUsedGb) = ReadMemory();

        return new SystemStats(
            Math.Clamp(cpu, 0, 1),
            IncludeCpuTemperature ? CpuTemperature.Read() : null,
            ram,
            ramUsedGb,
            IncludeGpu ? SampleGpuUsage() : null,
            IncludeGpu ? SampleGpuTemperature() : null);
    }

    public void Dispose()
    {
        CpuTemperature.Dispose();
        if (_pdhQuery != IntPtr.Zero) PdhCloseQuery(_pdhQuery);
        if (_pdhBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_pdhBuffer);
        if (_gpuLuid is not null) CloseAdapter(_gpuAdapter);
        _pdhQuery = IntPtr.Zero;
        _pdhBuffer = IntPtr.Zero;
        _gpuLuid = null;
    }

    /// <summary>Physical memory in use: fraction of total, and gigabytes.</summary>
    public static (double Usage, double UsedGb) ReadMemory()
    {
        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref memory);
        ulong used = memory.ullTotalPhys - memory.ullAvailPhys;
        return (memory.ullTotalPhys == 0 ? 0 : (double)used / memory.ullTotalPhys, used / 1073741824.0);
    }

    private static void ReadCpuTimes(out long idle, out long total)
    {
        GetSystemTimes(out idle, out long kernel, out long user);
        total = kernel + user;
    }

    // ---- GPU ---------------------------------------------------------------------------------

    private void EnsureGpu()
    {
        if (_gpuInitialized) return;
        _gpuInitialized = true;
        if (!TryOpenDiscreteAdapter()) return;

        if (PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) != 0)
        {
            _pdhQuery = IntPtr.Zero;
            return;
        }
        if (PdhAddEnglishCounterW(_pdhQuery, GpuEngineCounter, IntPtr.Zero, out _pdhCounter) != 0)
        {
            PdhCloseQuery(_pdhQuery);
            _pdhQuery = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Picks the adapter with the most dedicated VRAM — the graphics card rather than the CPU's
    /// integrated GPU (which only has a small carve-out) or a software adapter.
    /// </summary>
    private bool TryOpenDiscreteAdapter()
    {
        var enumeration = new D3DKMT_ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(ref enumeration) != 0 || enumeration.NumAdapters == 0) return false;

        int itemSize = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        enumeration.pAdapters = Marshal.AllocHGlobal(itemSize * (int)enumeration.NumAdapters);
        try
        {
            if (D3DKMTEnumAdapters2(ref enumeration) != 0) return false;

            ulong bestMemory = 0;
            for (int i = 0; i < enumeration.NumAdapters; i++)
            {
                var adapter = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(enumeration.pAdapters + i * itemSize);
                var segments = new D3DKMT_SEGMENTSIZEINFO();
                bool better = QueryAdapter(adapter.hAdapter, KMTQAITYPE_GETSEGMENTSIZE, ref segments)
                              && segments.DedicatedVideoMemorySize > bestMemory;
                if (!better)
                {
                    CloseAdapter(adapter.hAdapter);
                    continue;
                }

                if (_gpuLuid is not null) CloseAdapter(_gpuAdapter);
                bestMemory = segments.DedicatedVideoMemorySize;
                _gpuAdapter = adapter.hAdapter;
                _gpuLuid = $"luid_0x{(uint)adapter.AdapterLuid.HighPart:x8}_0x{adapter.AdapterLuid.LowPart:x8}";
            }
            return _gpuLuid is not null;
        }
        finally
        {
            Marshal.FreeHGlobal(enumeration.pAdapters);
        }
    }

    private double? SampleGpuTemperature()
    {
        if (_gpuLuid is null) return null;
        var perf = new D3DKMT_ADAPTER_PERFDATA(); // physical adapter 0
        if (!QueryAdapter(_gpuAdapter, KMTQAITYPE_ADAPTERPERFDATA, ref perf) || perf.Temperature == 0) return null;
        return perf.Temperature / 10.0;
    }

    /// <summary>
    /// PDH reports one instance per process and engine ("pid_42_luid_…_phys_0_eng_3_engtype_3D").
    /// Like Task Manager: add up each engine across processes and report the busiest engine.
    /// </summary>
    private double? SampleGpuUsage()
    {
        if (_pdhQuery == IntPtr.Zero || _gpuLuid is null) return null;
        if (PdhCollectQueryData(_pdhQuery) != 0) return null;

        const uint format = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
        uint size = _pdhBufferSize;
        int status = PdhGetFormattedCounterArrayW(_pdhCounter, format, ref size, out uint count, _pdhBuffer);
        if (status == PDH_MORE_DATA)
        {
            if (_pdhBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_pdhBuffer);
            _pdhBufferSize = size + size / 4; // headroom: process instances come and go
            _pdhBuffer = Marshal.AllocHGlobal((int)_pdhBufferSize);
            size = _pdhBufferSize;
            status = PdhGetFormattedCounterArrayW(_pdhCounter, format, ref size, out count, _pdhBuffer);
        }
        if (status != 0) return null;

        // PDH_FMT_COUNTERVALUE_ITEM_W = { wchar_t* name; { DWORD CStatus; double value; } }
        int itemSize = IntPtr.Size + 16;
        _engineLoad.Clear();
        for (int i = 0; i < count; i++)
        {
            IntPtr item = _pdhBuffer + i * itemSize;
            if ((uint)Marshal.ReadInt32(item, IntPtr.Size) > 1) continue; // only VALID_DATA / NEW_DATA

            string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
            if (name is null || !name.Contains(_gpuLuid, StringComparison.OrdinalIgnoreCase)) continue;
            int engine = name.IndexOf("_phys_", StringComparison.Ordinal);
            if (engine < 0) continue;

            string key = name[engine..];
            double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
            _engineLoad[key] = _engineLoad.GetValueOrDefault(key) + value;
        }

        double busiest = _engineLoad.Count == 0 ? 0 : _engineLoad.Values.Max();
        return Math.Clamp(busiest / 100, 0, 1);
    }

    private static bool QueryAdapter<T>(uint adapter, int type, ref T value) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            var query = new D3DKMT_QUERYADAPTERINFO
            {
                hAdapter = adapter,
                Type = type,
                pPrivateDriverData = buffer,
                PrivateDriverDataSize = (uint)size,
            };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) return false;
            value = Marshal.PtrToStructure<T>(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void CloseAdapter(uint adapter)
    {
        var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter };
        D3DKMTCloseAdapter(ref close);
    }
}
