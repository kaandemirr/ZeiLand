using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using static ZeiLand.Native.NativeMethods;
using static ZeiLand.Native.WindowNative;

namespace ZeiLand.Services;

/// <param name="App">The app that sent it, e.g. "WhatsApp".</param>
/// <param name="Title">Its first line, e.g. the sender.</param>
/// <param name="Body">The rest, on one line.</param>
internal sealed record IslandNotification(string App, string Title, string Body, ImageSource? Icon);

/// <summary>
/// New Windows notifications, to repeat on the island. Windows doesn't send unpackaged apps its "notification
/// added" event, but every toast it pops up uncloaks a window of the shell's notification host: that WinEvent
/// triggers one look at the notification list, and whatever is new since the last look is reported. Nothing is
/// polled, and toasts Windows holds back (do not disturb, full screen) aren't repeated either.
/// </summary>
internal sealed class NotificationWatcher : IDisposable
{
    private const string ToastHost = "ShellExperienceHost";
    private const string ToastWindowClass = "Windows.UI.Core.CoreWindow";

    // A notification older than this when first seen was already there (e.g. while the island was hidden).
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    private readonly WinEventProc _callback;
    private readonly HashSet<uint> _seen = [];
    private IntPtr _hook;
    private bool _checking, _checkAgain;

    public NotificationWatcher() => _callback = OnWinEvent; // Kept in a field so the delegate outlives the native hook.

    public event Action<IslandNotification>? Arrived;

    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value == Enabled) return;
            if (value) Start();
            else Stop();
        }
    }

    public void Dispose() => Stop();

    private async void Start()
    {
        _hook = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        // What's in the notification centre already isn't new.
        _seen.Clear();
        foreach (var notification in await ReadAsync()) _seen.Add(notification.Id);
    }

    private void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || !IsToastWindow(hwnd)) return;
        CheckAsync();
    }

    private static bool IsToastWindow(IntPtr hwnd)
    {
        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        return className.ToString() == ToastWindowClass && GetWindowProcessName(hwnd) == ToastHost;
    }

    private async void CheckAsync()
    {
        if (_checking)
        {
            _checkAgain = true;
            return;
        }
        _checking = true;
        try
        {
            do
            {
                _checkAgain = false;
                var notifications = await ReadAsync();
                if (!Enabled) return;
                foreach (var notification in notifications.OrderBy(n => n.CreationTime))
                {
                    if (!_seen.Add(notification.Id)) continue;
                    if (DateTimeOffset.Now - notification.CreationTime > Fresh) continue;
                    if (await DescribeAsync(notification) is { } shown) Arrived?.Invoke(shown);
                }
                // Forget the ones that are gone from the notification centre.
                _seen.IntersectWith(notifications.Select(n => n.Id));
            }
            while (_checkAgain);
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task<IReadOnlyList<UserNotification>> ReadAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return [];
            return await listener.GetNotificationsAsync(NotificationKinds.Toast);
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async Task<IslandNotification?> DescribeAsync(UserNotification notification)
    {
        try
        {
            // Apps still using the old toast templates (ToastText02 and the like) have no generic binding.
            var visual = notification.Notification.Visual;
            var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric) ?? visual.Bindings.FirstOrDefault();
            var lines = binding?.GetTextElements().Select(t => t.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? [];
            if (lines.Count == 0) return null;
            string app = notification.AppInfo?.DisplayInfo?.DisplayName ?? "";
            return new IslandNotification(app, lines[0]!, string.Join(" ", lines.Skip(1)).ReplaceLineEndings(" "),
                await IconAsync(notification));
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<ImageSource?> IconAsync(UserNotification notification)
    {
        try
        {
            if (notification.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64)) is not { } logo) return null;
            using var stream = (await logo.OpenReadAsync()).AsStreamForRead();
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is COMException or IOException or NotSupportedException or UnauthorizedAccessException
            or FileFormatException)
        {
            return null;
        }
    }
}
