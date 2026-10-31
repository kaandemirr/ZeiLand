using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZeiLand.Controls;

/// <summary>
/// An animated GIF that only plays while it's on screen. The frames are decoded and put together once, on a
/// background thread, at the size they're shown at; playing is just swapping a few small bitmaps.
/// </summary>
public sealed class GifImage : Image
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private GifAnimation? _animation;
    private int _frame;
    private int _loads;

    public GifImage()
    {
        Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer.Tick += (_, _) => NextFrame();
        IsVisibleChanged += (_, _) => UpdatePlaying();
    }

    public bool HasAnimation => _animation is not null;

    /// <summary>
    /// Shows the GIF at the largest size that fits the box, with rounded corners; null clears it.
    /// False if the file can't be shown (not a GIF, damaged, or too big).
    /// </summary>
    public async Task<bool> LoadAsync(string? path, double maxWidth, double maxHeight, double cornerRadius)
    {
        int load = ++_loads;
        var animation = path is null ? null
            : await GifAnimation.LoadAsync(path, maxWidth, maxHeight, VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (load != _loads) return animation is not null; // a newer one is on its way

        _animation = animation;
        _frame = 0;
        Source = animation?.Frames[0];
        Width = animation?.Width ?? 0;
        Height = animation?.Height ?? 0;
        Clip = animation is null ? null
            : new RectangleGeometry(new Rect(0, 0, animation.Width, animation.Height), cornerRadius, cornerRadius).Frozen();
        UpdatePlaying();
        return animation is not null;
    }

    private void UpdatePlaying()
    {
        if (IsVisible && _animation is { Frames.Length: > 1 } animation)
        {
            if (_timer.IsEnabled) return;
            _timer.Interval = animation.Delays[_frame];
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void NextFrame()
    {
        if (_animation is not { } animation) return;
        _frame = (_frame + 1) % animation.Frames.Length;
        Source = animation.Frames[_frame];
        _timer.Interval = animation.Delays[_frame];
    }
}

/// <summary>A GIF's frames, ready to show: each one complete (GIF frames are usually patches) and scaled.</summary>
internal sealed record GifAnimation(BitmapSource[] Frames, TimeSpan[] Delays, double Width, double Height)
{
    private const long MaxFileBytes = 20L << 20;
    private const int MaxSourceFrames = 1000;

    // Longer GIFs keep every second (third, …) frame, shown for as long as the ones it stands for.
    private const int MaxFrames = 150;

    public static Task<GifAnimation?> LoadAsync(string path, double maxWidth, double maxHeight, double scale)
    {
        var result = new TaskCompletionSource<GifAnimation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Load(path, maxWidth, maxHeight, scale));
            }
            catch (Exception)
            {
                // Whatever the decoder trips over in a user's file just means there's no GIF to show.
                result.TrySetResult(null);
            }
            finally
            {
                // The full-size work bitmaps live in native memory the GC barely notices (hundreds of MB for a
                // big GIF): hand it back now rather than whenever.
                GC.Collect();
                Dispatcher.CurrentDispatcher.InvokeShutdown(); // the drawing visuals made this thread one
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return result.Task;
    }

    private static GifAnimation? Load(string path, double maxWidth, double maxHeight, double scale)
    {
        if (new FileInfo(path).Length > MaxFileBytes) return null;
        // Frames are decoded one by one as they're drawn, not all kept at full size up front.
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        int count = decoder.Frames.Count;
        if (count is 0 or > MaxSourceFrames) return null;

        // The "logical screen" every frame is drawn onto.
        int screenWidth = Query(decoder.Metadata, "/logscrdesc/Width", decoder.Frames[0].PixelWidth);
        int screenHeight = Query(decoder.Metadata, "/logscrdesc/Height", decoder.Frames[0].PixelHeight);
        if (screenWidth <= 0 || screenHeight <= 0) return null;
        var screen = new Rect(0, 0, screenWidth, screenHeight);

        double fit = Math.Min(maxWidth / screenWidth, maxHeight / screenHeight);
        double width = Math.Round(screenWidth * fit), height = Math.Round(screenHeight * fit);
        int pixelWidth = Math.Max(1, (int)Math.Round(width * scale)), pixelHeight = Math.Max(1, (int)Math.Round(height * scale));
        int step = (count + MaxFrames - 1) / MaxFrames;

        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        BitmapSource? canvas = null;
        for (int i = 0; i < count; i++)
        {
            var frame = decoder.Frames[i];
            var metadata = frame.Metadata as BitmapMetadata;
            var patch = new Rect(Query(metadata, "/imgdesc/Left", 0), Query(metadata, "/imgdesc/Top", 0),
                Query(metadata, "/imgdesc/Width", frame.PixelWidth), Query(metadata, "/imgdesc/Height", frame.PixelHeight));
            int delay = Query(metadata, "/grctlext/Delay", 10); // hundredths of a second; 0 or 1 means "fast", i.e. 0.1 s
            var duration = TimeSpan.FromMilliseconds(delay <= 1 ? 100 : delay * 10);

            var previous = canvas;
            var whole = Render(screenWidth, screenHeight, 96, dc =>
            {
                if (previous is not null) dc.DrawImage(previous, screen);
                dc.DrawImage(frame, patch);
            });

            if (i % step == 0)
            {
                frames.Add(Render(pixelWidth, pixelHeight, 96 * scale, dc => dc.DrawImage(whole, new Rect(0, 0, width, height))));
                delays.Add(duration);
            }
            else
            {
                delays[^1] += duration;
            }

            // What the next frame is drawn onto.
            canvas = Query(metadata, "/grctlext/Disposal", 0) switch
            {
                2 => Render(screenWidth, screenHeight, 96, dc => // this patch back to transparent
                {
                    dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(screen), new RectangleGeometry(patch)));
                    dc.DrawImage(whole, screen);
                    dc.Pop();
                }),
                3 => previous, // back to how it was before this frame
                _ => whole,
            };
        }
        return new GifAnimation([.. frames], [.. delays], width, height);
    }

    private static BitmapSource Render(int pixelWidth, int pixelHeight, double dpi, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static int Query(ImageMetadata? metadata, string query, int fallback)
    {
        try
        {
            return (metadata as BitmapMetadata)?.GetQuery(query) is { } value ? Convert.ToInt32(value) : fallback;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or InvalidCastException)
        {
            return fallback;
        }
    }
}
