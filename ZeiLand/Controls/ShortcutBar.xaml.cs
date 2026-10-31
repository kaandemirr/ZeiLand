using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZeiLand.Services;
using ZeiLand.Settings;

namespace ZeiLand.Controls;

/// <summary>The user's shortcuts as a row of icon tiles in the open island, plus "+" to add more.</summary>
public partial class ShortcutBar : UserControl
{
    private const string WebsiteGlyph = "";
    private const string AddGlyph = "";
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public ShortcutBar() => InitializeComponent();

    /// <summary>"+" was clicked: open the shortcut settings.</summary>
    public event Action? AddRequested;

    internal void Show(IReadOnlyList<ShortcutItem> shortcuts)
    {
        Tiles.Children.Clear();
        foreach (var shortcut in shortcuts) Tiles.Children.Add(CreateTile(shortcut));
        if (shortcuts.Count < AppSettings.MaxShortcuts)
        {
            var add = Tile(Glyph(AddGlyph, 13), Loc.T("AddShortcut"));
            add.Opacity = 0.7;
            add.Click += (_, _) => AddRequested?.Invoke();
            Tiles.Children.Add(add);
        }
    }

    private Button CreateTile(ShortcutItem shortcut)
    {
        UIElement content = Shortcuts.Icon(shortcut, 48) is { } icon
            ? new Image { Source = icon, Width = 22, Height = 22 }
            : Glyph(WebsiteGlyph, 16);
        RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.HighQuality);
        var tile = Tile(content, shortcut.Name);
        tile.Click += (_, _) =>
        {
            Shortcuts.Launch(shortcut);
            Pop(tile);
        };
        return tile;
    }

    private Button Tile(object content, string toolTip) =>
        new() { Style = (Style)Resources["Tile"], Content = content, ToolTip = toolTip, RenderTransform = new ScaleTransform(1, 1) };

    private static TextBlock Glyph(string glyph, double size) =>
        new() { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A quick squeeze, so a click visibly did something while the app starts.</summary>
    private static void Pop(Button tile)
    {
        var squeeze = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        };
        var scale = (ScaleTransform)tile.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, squeeze);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, squeeze);
    }
}
