using System.Runtime.InteropServices;

namespace ZeiLand.Native;

// CPU/RAM (kernel32), GPU temperature (D3DKMT thunks in gdi32 — the same source Task Manager uses)
// and GPU engine load (PDH performance counters). None of these need administrator rights.
internal static class SystemInfoNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    // ---- D3DKMT ------------------------------------------------------------------------------

    public const int KMTQAITYPE_GETSEGMENTSIZE = 3;
    public const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public LUID AdapterLuid;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_SEGMENTSIZEINFO
    {
        public ulong DedicatedVideoMemorySize;
        public ulong DedicatedSystemMemorySize;
        public ulong SharedSystemMemorySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature; // deci-°C
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 data);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO data);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER data);

    // ---- PDH ---------------------------------------------------------------------------------

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    // English counter names work on any UI language (counter names are localized on Turkish Windows).
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
        out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    public static extern int PdhCloseQuery(IntPtr query);
}
