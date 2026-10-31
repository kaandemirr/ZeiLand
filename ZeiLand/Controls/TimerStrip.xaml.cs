using System.Windows;
using System.Windows.Controls;
using ZeiLand.Services;

namespace ZeiLand.Controls;

/// <summary>The running timer or stopwatch inside the expanded island, with its controls.</summary>
public partial class TimerStrip : UserControl
{
    private const string PauseGlyphText = "";
    private const string ResumeGlyphText = "";

    private TimerService? _timer;

    public TimerStrip()
    {
        InitializeComponent();
        AddButton.Click += (_, _) => _timer?.Add(TimeSpan.FromMinutes(1));
        PauseButton.Click += (_, _) => _timer?.TogglePause();
        StopButton.Click += (_, _) => _timer?.Stop();
    }

    internal void Attach(TimerService timer) => _timer = timer;

    /// <summary>Redraws the time; the island calls this when the second changes and on every timer change.</summary>
    public void Render()
    {
        if (_timer is not { IsActive: true } timer) return;
        bool countdown = timer.Mode == TimerMode.Countdown;
        TimeText.Text = TimerService.Format(timer.Display);
        TimeText.Opacity = timer.IsPaused ? 0.5 : 1;
        LabelText.Text = Loc.T(timer.IsPaused ? "Paused" : countdown ? "TimerTitle" : "Stopwatch");
        PauseGlyph.Text = timer.IsPaused ? ResumeGlyphText : PauseGlyphText;
        PauseButton.ToolTip = Loc.T(timer.IsPaused ? "Resume" : "Pause");
        AddButton.Visibility = countdown ? Visibility.Visible : Visibility.Collapsed;
    }
}
