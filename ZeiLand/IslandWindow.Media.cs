using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZeiLand.Services;

namespace ZeiLand;

// Expanded player extras: seeking on the progress bar, back / ahead buttons, and the playing app's icon on the artwork.
public partial class IslandWindow
{
    private const int SourceIconSize = 32;

    private bool _seeking;

    private void InitializeSeeking()
    {
        // The bar thickens under the pointer to show it can be grabbed.
        ProgressHitArea.MouseEnter += (_, _) => ProgressTrack.Height = _media.CanSeek ? 6 : 4;
        ProgressHitArea.MouseLeave += (_, _) =>
        {
            if (!_seeking) ProgressTrack.Height = 4;
        };
        ProgressHitArea.MouseLeftButtonDown += (_, e) =>
        {
            if (!_media.CanSeek || _media.GetTimeline() is null) return;
            _seeking = ProgressHitArea.CaptureMouse();
            PreviewSeek(e.GetPosition(ProgressHitArea).X);
            e.Handled = true; // Not a click on the island itself.
        };
        ProgressHitArea.MouseMove += (_, e) =>
        {
            if (_seeking) PreviewSeek(e.GetPosition(ProgressHitArea).X);
        };
        ProgressHitArea.MouseLeftButtonUp += async (_, e) =>
        {
            if (!_seeking) return;
            double fraction = SeekFraction(e.GetPosition(ProgressHitArea).X);
            EndSeeking();
            if (_media.GetTimeline() is { } timeline) await _media.SeekAsync(timeline.Duration * fraction);
        };
        ProgressHitArea.LostMouseCapture += (_, _) => EndSeeking();
    }

    private void EndSeeking()
    {
        if (!_seeking) return;
        _seeking = false;
        ProgressHitArea.ReleaseMouseCapture();
        if (!ProgressHitArea.IsMouseOver) ProgressTrack.Height = 4;
    }

    /// <summary>Moves the bar and times to where the pointer is; the player is told on release.</summary>
    private void PreviewSeek(double x)
    {
        if (_media.GetTimeline() is not { } timeline) return;
        double fraction = SeekFraction(x);
        ProgressScale.ScaleX = fraction;
        ElapsedText.Text = FormatTime(timeline.Duration * fraction);
        RemainingText.Text = "-" + FormatTime(timeline.Duration * (1 - fraction));
    }

    private double SeekFraction(double x) => Math.Clamp(x / Math.Max(ProgressHitArea.ActualWidth, 1), 0, 1);

    /// <summary>Back / ahead beside the controls when they're on and the player can jump (most browsers, many apps).</summary>
    private void UpdateSkipButtons()
    {
        int seconds = _settings.SkipSeconds;
        SkipBackText.Text = SkipForwardText.Text = seconds.ToString(Loc.Culture);
        SkipBackButton.ToolTip = Loc.F("SkipBack", seconds);
        SkipForwardButton.ToolTip = Loc.F("SkipAhead", seconds);
        var visibility = seconds > 0 && _media.CanSeek ? Visibility.Visible : Visibility.Collapsed;
        if (SkipBackButton.Visibility == visibility) return;
        SkipBackButton.Visibility = SkipForwardButton.Visibility = visibility;
        UpdateExpandedSizes();
    }

    /// <summary>Wide enough for the controls to stay centred, clear of the GIF on the left and the quick actions on the right.</summary>
    private double MediaViewWidth()
    {
        var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
        PlayerControls.Measure(any);
        MediaGif.Measure(any);
        MediaActions.Measure(any);
        double side = Math.Max(MediaGif.DesiredSize.Width, MediaActions.DesiredSize.Width) + 6;
        return Math.Clamp(36 + PlayerControls.DesiredSize.Width + 2 * side, 380, Width - 40); // 18 + 18: the view's padding
    }

    private void UpdateSourceIcon()
    {
        _volume.SetMeterApp(_media.SourceAppId); // The level bars follow the player, not the whole mix.
        var icon = _media.SourceAppId is { } app ? AppIcons.ForAppId(app, SourceIconSize) : null;
        foreach (var image in new[] { CompactSourceIcon, MediaSourceIcon })
        {
            image.Source = icon;
            ((Border)image.Parent).Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        }
        ProgressHitArea.Cursor = _media.CanSeek ? Cursors.Hand : null;
    }
}
