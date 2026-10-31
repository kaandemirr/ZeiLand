using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace ZeiLand.Services;

/// <summary>
/// "Start with Windows" through a Task Scheduler logon task with highest privileges, so the island
/// starts in administrator mode (for the CPU temperature) without a UAC prompt at every logon.
/// </summary>
internal static class Autostart
{
    private const string TaskName = "ZeiLand";

    private static bool? _isEnabled;

    public static bool IsEnabled => _isEnabled ??= Schtasks($"/Query /TN \"{TaskName}\"", elevate: false) == 0;

    /// <summary>Creates or removes the logon task. May show a UAC prompt; returns false if it didn't happen.</summary>
    public static bool SetEnabled(bool enabled)
    {
        bool done = enabled ? CreateTask() : Schtasks($"/Delete /TN \"{TaskName}\" /F", elevate: true) == 0;
        if (!done) return false;

        _isEnabled = enabled;
        return true;
    }

    private static bool CreateTask()
    {
        // schtasks wants the definition as a UTF-16 file.
        var path = Path.Combine(Path.GetTempPath(), "ZeiLand-task.xml");
        try
        {
            File.WriteAllText(path, TaskXml(), Encoding.Unicode);
            return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{path}\" /F", elevate: true) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static string TaskXml()
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string exe = SecurityElement.Escape(Environment.ProcessPath!);
        // No execution time limit (the default stops tasks after 72 h) and normal priority (the default is below normal).
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>ZeiLand</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT3S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command></Exec>
              </Actions>
            </Task>
            """;
    }

    private static int Schtasks(string arguments, bool elevate)
    {
        if (elevate) return Elevation.RunHidden("schtasks.exe", arguments);

        var info = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info);
        if (process is null) return -1;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
