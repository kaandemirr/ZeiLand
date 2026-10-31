using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using ZeiLand.Settings;

namespace ZeiLand.Services;

/// <summary>An app from the Start menu's "All apps" list.</summary>
internal sealed record InstalledApp(string Name, string Id);

/// <summary>Opening the user's shortcuts, their icons, and the installed apps to pick from.</summary>
internal static class Shortcuts
{
    /// <summary>
    /// Opens it the way Explorer would. Going through explorer.exe also matters for rights: the island
    /// usually runs as administrator (for the CPU temperature), but explorer.exe hands the request to the
    /// running shell, so the app starts as the signed-in user, not elevated.
    /// </summary>
    public static void Launch(ShortcutItem item)
    {
        string target = item.Kind == ShortcutKind.App ? $@"shell:AppsFolder\{item.Target}" : item.Target;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Win32Exception)
        {
        }
    }

    /// <summary>The app's, file's or folder's icon; null for a website (shown with a globe).</summary>
    public static ImageSource? Icon(ShortcutItem item, int size) => item.Kind switch
    {
        ShortcutKind.App => AppIcons.ForAppId(item.Target, size),
        ShortcutKind.File or ShortcutKind.Folder => AppIcons.ForPath(item.Target, size),
        _ => null,
    };

    /// <summary>
    /// Everything in the Start menu's "All apps" (desktop and Store apps alike), sorted by name. Read through
    /// the shell on a background STA thread; takes a moment, so it's only done when the picker opens.
    /// </summary>
    public static Task<List<InstalledApp>> GetInstalledAppsAsync()
    {
        var result = new TaskCompletionSource<List<InstalledApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var apps = new List<InstalledApp>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic entry in folder.Items())
                {
                    string name = entry.Name;
                    string id = entry.Path;
                    // Skip links to websites and documentation pinned into the Start menu.
                    if (name.Length > 0 && !id.Contains("://")) apps.Add(new InstalledApp(name, id));
                }
            }
            catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
            }
            result.SetResult(apps
                .DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return result.Task;
    }
}
