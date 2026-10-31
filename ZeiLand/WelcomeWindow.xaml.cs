using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZeiLand.Services;
using ZeiLand.Settings;

namespace ZeiLand;

/// <summary>A short guide shown after the first install (and from Settings → Hakkında): a few pages of tips, then two choices.</summary>
public partial class WelcomeWindow : Window
{
    // The hero island's shapes (width, height): resting tab, compact player, open, compact again.
    private static readonly (double Width, double Height)[] HeroShapes = [(96, 12), (230, 34), (300, 112), (230, 34)];
    private static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }.Frozen();
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut }.Frozen();
    private static readonly Brush DotBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)).Frozen();

    private readonly AppSettings _settings;
    private readonly List<FrameworkElement> _pages;
    private readonly List<Border> _dots = [];
    private readonly DispatcherTimer _hero = new() { Interval = TimeSpan.FromSeconds(1.7) };
    private int _page;
    private int _heroStep;

    internal WelcomeWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _pages = Pages.Children.OfType<FrameworkElement>().ToList();
        foreach (var page in _pages)
        {
            page.RenderTransform = new TranslateTransform();
            var dot = new Border { Height = 7, Width = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(3, 0, 3, 0), Background = DotBrush };
            _dots.Add(dot);
            Dots.Children.Add(dot);
        }

        WriteTexts();
        Loc.Changed += WriteTexts;

        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        AutostartSwitch.Click += async (_, _) =>
        {
            bool wanted = AutostartSwitch.IsChecked == true;
            AutostartSwitch.IsEnabled = false;
            bool done = await Task.Run(() => Autostart.SetEnabled(wanted)); // may ask UAC
            AutostartSwitch.IsChecked = done ? wanted : !wanted;
            AutostartSwitch.IsEnabled = true;
        };
        AutoUpdateSwitch.IsChecked = settings.AutoUpdate;
        AutoUpdateSwitch.Click += (_, _) => _settings.AutoUpdate = AutoUpdateSwitch.IsChecked == true;

        NextButton.Click += (_, _) => Next();
        BackButton.Click += (_, _) => Go(_page - 1);
        CloseButton.Click += (_, _) => Close();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove(); // buttons handle their own clicks
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Right or Key.Enter) Next();
            else if (e.Key == Key.Left) Go(_page - 1);
            else if (e.Key == Key.Escape) Close();
        };

        _hero.Tick += (_, _) => StepHero();
        Closed += (_, _) =>
        {
            _hero.Stop();
            Loc.Changed -= WriteTexts;
            _settings.WelcomeShown = true;
        };
        Go(0);
    }

    /// <summary>The texts written in code (the rest follow the language by themselves).</summary>
    private void WriteTexts()
    {
        string shortcut = _settings.MicMuteHotkey;
        MicrophoneTip.Text = Loc.F("OutputMicText", shortcut.Length > 0 ? Loc.F("OutputMicShortcut", shortcut.Replace("+", " + ")) : "");
        NextButton.Content = Loc.T(_page == _pages.Count - 1 ? "Start" : "Next");
    }

    private void Next()
    {
        if (_page == _pages.Count - 1) Close();
        else Go(_page + 1);
    }

    private void Go(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        bool forward = index >= _page;
        var outgoing = _pages[_page];
        var incoming = _pages[index];
        if (!ReferenceEquals(outgoing, incoming)) outgoing.Visibility = Visibility.Hidden;
        _page = index;

        incoming.Visibility = Visibility.Visible;
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(forward ? 36 : -36, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = EaseOut });

        for (int i = 0; i < _dots.Count; i++)
        {
            _dots[i].Background = i == index ? Brushes.White : DotBrush;
            _dots[i].BeginAnimation(WidthProperty, new DoubleAnimation(i == index ? 20 : 7, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
        }
        BackButton.Visibility = index > 0 ? Visibility.Visible : Visibility.Hidden;
        WriteTexts();

        // The little island only morphs while it's on screen.
        if (index == 0)
        {
            _heroStep = 0;
            StepHero();
            _hero.Start();
        }
        else
        {
            _hero.Stop();
        }
    }

    /// <summary>The hero island cycles through its shapes like the real one does.</summary>
    private void StepHero()
    {
        _heroStep = (_heroStep + 1) % HeroShapes.Length;
        var (width, height) = HeroShapes[_heroStep];
        var duration = TimeSpan.FromMilliseconds(520);
        HeroIsland.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = Spring });
        HeroIsland.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = Spring });
        double radius = Math.Min(height / 2, 26);
        HeroIsland.BeginAnimation(System.Windows.Shapes.Rectangle.RadiusXProperty, new DoubleAnimation(radius, duration) { EasingFunction = EaseOut });
        HeroIsland.BeginAnimation(System.Windows.Shapes.Rectangle.RadiusYProperty, new DoubleAnimation(radius, duration) { EasingFunction = EaseOut });
        // The microphone / camera dots show on the compact island.
        HeroDots.BeginAnimation(OpacityProperty, new DoubleAnimation(height is > 20 and < 60 ? 1 : 0, TimeSpan.FromMilliseconds(300)));
    }
}
