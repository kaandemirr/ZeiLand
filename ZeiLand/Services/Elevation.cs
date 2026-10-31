using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ZeiLand.Services;

/// <summary>Administrator mode, needed only for reading the CPU temperature through the PawnIO driver.</summary>
internal static class Elevation
{
    /// <summary>Passed to a new instance that replaces the current one, so it waits for the old one to exit.</summary>
    public const string RestartArgument = "--restart";

    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts an elevated copy of the app (UAC prompt). False if the user declined.</summary>
    public static bool StartElevatedInstance()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, RestartArgument)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt cancelled.
        }
    }

    /// <summary>Runs a command-line tool hidden, elevating it first when this process isn't. Returns its exit code, or -1.</summary>
    public static int RunHidden(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments) { WindowStyle = ProcessWindowStyle.Hidden };
        if (IsElevated)
        {
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
        }
        else
        {
            info.UseShellExecute = true;
            info.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null) return -1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            return -1;
        }
    }
}
