using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZeiLand.Controls;

/// <summary>Timer, sound device and microphone buttons in the expanded island; the settings pick which show.</summary>
public partial class QuickActions : UserControl
{
    private const string MicrophoneGlyph = "";
    private const string MicrophoneOffGlyph = "";

    private static readonly Brush NormalBackground = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush NormalForeground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush MutedBackground = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Brush MutedForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Brush TimerBackground = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x0A)).Frozen();
    private static readonly Brush TimerForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)).Frozen();

    private bool _microphoneAvailable;
    private bool _microphoneChosen = true;

    public QuickActions()
    {
        InitializeComponent();
        TimerButton.Click += (_, _) => TimerClicked?.Invoke();
        OutputButton.Click += (_, _) => OutputClicked?.Invoke();
        MicrophoneButton.Click += (_, _) => MicrophoneClicked?.Invoke();
    }

    public event Action? TimerClicked;
    public event Action? OutputClicked;
    public event Action? MicrophoneClicked;

    /// <summary>The buttons picked in the settings; the microphone one also needs a microphone.</summary>
    public void Choose(bool timer, bool sound, bool microphone)
    {
        TimerButton.Visibility = timer ? Visibility.Visible : Visibility.Collapsed;
        OutputButton.Visibility = sound ? Visibility.Visible : Visibility.Collapsed;
        _microphoneChosen = microphone;
        MicrophoneButton.Visibility = _microphoneChosen && _microphoneAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowTimer(bool active)
    {
        TimerButton.Background = active ? TimerBackground : NormalBackground;
        TimerButton.Foreground = active ? TimerForeground : NormalForeground;
    }

    /// <param name="shortcut">The global shortcut, for the tooltip; null if none.</param>
    public void ShowMicrophone(bool available, bool muted, string? shortcut)
    {
        _microphoneAvailable = available;
        MicrophoneButton.Visibility = available && _microphoneChosen ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneButton.Content = muted ? MicrophoneOffGlyph : MicrophoneGlyph;
        MicrophoneButton.Background = muted ? MutedBackground : NormalBackground;
        MicrophoneButton.Foreground = muted ? MutedForeground : NormalForeground;
        string action = Loc.T(muted ? "MicUnmute" : "MicMute");
        MicrophoneButton.ToolTip = string.IsNullOrEmpty(shortcut) ? action : $"{action} ({shortcut})";
    }
}
