using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static ZeiLand.Native.WindowNative;

namespace ZeiLand.Services;

/// <summary>
/// One app: every process with the same name in the user's session (e.g. every chrome.exe), plus the
/// windowless helper processes they started (Steam's steamwebhelper, an app's WebView2...).
/// </summary>
internal sealed record AppUsage(
    string Name,
    string DisplayName,
    string? Path,
    int ProcessCount,
    long MemoryBytes,
    double CpuUsage,
    bool HasWindow,
    int[] ProcessIds);

/// <summary>
/// Finds the apps using the most memory/CPU so the user can close them. Only runs while the
/// optimize panel is open. Services (other sessions) and core Windows processes are never listed.
/// </summary>
internal sealed class ProcessMonitor
{
    // Closing any of these breaks the desktop session or security; never offer them.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "explorer", "fontdrvhost", "Memory Compression", "sihost", "ctfmon", "audiodg", "conhost",
        "RuntimeBroker", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchIndexer",
        "TextInputHost", "LockApp", "ApplicationFrameHost", "taskhostw", "dllhost", "WmiPrvSE", "spoolsv",
        "SecurityHealthSystray", "SecurityHealthService", "MsMpEng", "NisSrv", "smartscreen", "UserOOBEBroker",
        "ShellHost", "WidgetService", "dasHost", "WUDFHost", "unsecapp", "SgrmBroker", "ZeiLand",
    };

    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly int _ownId = Environment.ProcessId;
    private readonly Dictionary<string, (string? Description, string? Path)> _details = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, TimeSpan> _lastCpu = new();
    private long _lastTimestamp;

    /// <summary>Takes the CPU-time baseline; call before the first <see cref="Sample"/>.</summary>
    public void Begin()
    {
        _lastCpu = ReadCpuTimes(out var processes);
        _lastTimestamp = Stopwatch.GetTimestamp();
        foreach (var process in processes) process.Dispose();
    }

    public IReadOnlyList<AppUsage> Sample(int top)
    {
        var cpuTimes = ReadCpuTimes(out var processes);
        double elapsed = Stopwatch.GetElapsedTime(_lastTimestamp).TotalSeconds * Environment.ProcessorCount;
        _lastTimestamp = Stopwatch.GetTimestamp();

        var byId = processes.ToDictionary(p => p.Id);
        var parents = ReadParents();
        var windowed = WindowedProcessIds();

        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in processes)
        {
            long memory;
            try
            {
                memory = process.PrivateMemorySize64;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                continue; // Exited meanwhile.
            }

            double cpu = 0;
            if (elapsed > 0 && cpuTimes.TryGetValue(process.Id, out var now) && _lastCpu.TryGetValue(process.Id, out var before))
                cpu = Math.Max(0, (now - before).TotalSeconds / elapsed);

            var root = FindRoot(process, byId, parents, windowed);
            if (!groups.TryGetValue(root.ProcessName, out var group))
                groups[root.ProcessName] = group = new Group(root.Id);
            group.Memory += memory;
            group.Cpu += cpu;
            group.Members.Add(process.Id);
            group.HasWindow |= windowed.Contains(process.Id);
        }
        _lastCpu = cpuTimes;

        // File-description lookups are comparatively slow, so only do them for the shortlist.
        var result = groups
            .OrderByDescending(g => g.Value.Memory + (long)(g.Value.Cpu * 4_000_000_000)) // 10% CPU weighs like 400 MB
            .Take(top)
            .Select(g =>
            {
                var (description, path) = Describe(g.Key, g.Value.RootId);
                return new AppUsage(g.Key, description ?? g.Key, path, g.Value.Members.Count, g.Value.Memory,
                    Math.Min(g.Value.Cpu, 1), g.Value.HasWindow, g.Value.Members.ToArray());
            })
            .ToList();

        foreach (var process in processes) process.Dispose();
        return result;
    }

    /// <summary>
    /// Closes an app. Politely (asks its windows to close, so it can offer to save) unless
    /// <paramref name="force"/> is set, which terminates the processes outright.
    /// </summary>
    public static void Close(AppUsage app, bool force)
    {
        foreach (int id in app.ProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (force) process.Kill();
                else process.CloseMainWindow();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already gone, or not ours to close.
            }
        }
    }

    /// <summary>Whether any process with this name is still running in the user's session.</summary>
    public bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        bool running = processes.Any(p => p.SessionId == _sessionId);
        foreach (var process in processes) process.Dispose();
        return running;
    }

    /// <summary>
    /// The process a helper belongs to: walks up the parents while the process has no window of its own
    /// and its parent is a listed process. Apps with a window (a game Steam started) stay on their own.
    /// </summary>
    private static Process FindRoot(Process process, Dictionary<int, Process> byId, Dictionary<int, int> parents,
        HashSet<int> windowed)
    {
        var current = process;
        for (int depth = 0; depth < 16 && !windowed.Contains(current.Id); depth++)
        {
            if (!parents.TryGetValue(current.Id, out int parentId) || !byId.TryGetValue(parentId, out var parent)) break;
            current = parent;
        }
        return current;
    }

    private Dictionary<int, TimeSpan> ReadCpuTimes(out List<Process> processes)
    {
        processes = new List<Process>();
        var times = new Dictionary<int, TimeSpan>();
        foreach (var process in Process.GetProcesses())
        {
            if (process.SessionId != _sessionId || process.Id == _ownId || Protected.Contains(process.ProcessName))
            {
                process.Dispose();
                continue;
            }
            try
            {
                times[process.Id] = process.TotalProcessorTime;
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // Elevated or protected process we can't open; still listed by memory.
            }
            processes.Add(process);
        }
        return times;
    }

    /// <summary>Parent process ids from one Toolhelp snapshot.</summary>
    private static Dictionary<int, int> ReadParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1)) return parents;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                parents[entry.th32ProcessID] = entry.th32ParentProcessID;
        }
        finally
        {
            Native.NativeMethods.CloseHandle(snapshot);
        }
        return parents;
    }

    /// <summary>Processes owning a visible top-level window — one pass instead of a window search per process.</summary>
    private static HashSet<int> WindowedProcessIds()
    {
        var ids = new HashSet<int>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                ids.Add((int)processId);
            }
            return true;
        }, IntPtr.Zero);
        return ids;
    }

    /// <summary>"Google Chrome" instead of "chrome", from the executable's version info (cached per name).</summary>
    private (string? Description, string? Path) Describe(string name, int rootId)
    {
        if (_details.TryGetValue(name, out var cached)) return cached;

        string? path = GetProcessPath(rootId), description = null;
        if (path is not null)
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            }
            catch (FileNotFoundException)
            {
            }
        }
        var details = (string.IsNullOrEmpty(description) ? null : description, path);
        _details[name] = details;
        return details;
    }

    private sealed class Group(int rootId)
    {
        public int RootId { get; } = rootId;
        public long Memory { get; set; }
        public double Cpu { get; set; }
        public bool HasWindow { get; set; }
        public List<int> Members { get; } = [];
    }
}
