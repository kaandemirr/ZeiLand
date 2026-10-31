using System.Runtime.InteropServices;

namespace ZeiLand.Native;

// Media Foundation's sensor activity monitor (Windows 10 1703+): the camera frame server reports which
// processes are streaming from which camera. Definitions from mfidl.h.

[ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivityMonitor
{
    void Start();
    void Stop();
}

[ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivitiesReportCallback
{
    [PreserveSig]
    int OnActivitiesReport(IMFSensorActivitiesReport report);
}

[ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivitiesReport
{
    void GetCount(out uint count);
    void GetActivityReport(uint index, out IMFSensorActivityReport report);
}

[ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivityReport
{
    void GetFriendlyName([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] name, uint size, out uint written);
    void GetSymbolicLink([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] link, uint size, out uint written);
    void GetProcessCount(out uint count);
    void GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
}

[ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorProcessActivity
{
    void GetProcessId(out uint processId);
    void GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
    void GetStreamingMode(out int mode);
    void GetReportTime(out long fileTime);
}

internal static class SensorActivity
{
    public const int MF_VERSION = 0x00020070;
    public const int MFSTARTUP_LITE = 1;

    [DllImport("mfplat.dll")]
    public static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll")]
    public static extern int MFShutdown();

    [DllImport("mfsensorgroup.dll")]
    public static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback,
        out IMFSensorActivityMonitor monitor);
}
