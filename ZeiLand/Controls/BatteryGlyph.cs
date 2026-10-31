using System.Windows;
using System.Windows.Media;

namespace ZeiLand.Controls;

/// <summary>Small battery icon drawn directly in OnRender (no child elements).</summary>
internal sealed class BatteryGlyph : FrameworkElement
{
    private static readonly Brush OutlineBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush ChargingBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)).Frozen();
    private static readonly Brush LowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).Frozen();
    private static readonly Pen OutlinePen = new Pen(OutlineBrush, 1).Frozen();
    private static readonly Pen BoltPen = new Pen(Brushes.Black, 1.2) { LineJoin = PenLineJoin.Round }.Frozen();
    private static readonly Geometry Bolt = Geometry.Parse("M5.5,0 L0,7.5 H3.6 L2.8,13 L8.5,5 H4.9 Z").Frozen();

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(BatteryGlyph),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsChargingProperty = DependencyProperty.Register(
        nameof(IsCharging), typeof(bool), typeof(BatteryGlyph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public bool IsCharging
    {
        get => (bool)GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width < 8 || height < 6) return;

        const double nub = 2;
        var body = new Rect(0.5, 0.5, width - nub - 1.5, height - 1);
        dc.DrawRoundedRectangle(null, OutlinePen, body, 3, 3);
        dc.DrawRoundedRectangle(OutlineBrush, null, new Rect(width - nub, height * 0.3, nub, height * 0.4), 1, 1);

        double level = Math.Clamp(Level, 0, 1);
        var fill = body;
        fill.Inflate(-2, -2);
        fill.Width *= level;
        var brush = IsCharging ? ChargingBrush : level <= 0.2 ? LowBrush : Brushes.White;
        if (fill.Width > 0.5) dc.DrawRoundedRectangle(brush, null, fill, 1.5, 1.5);

        if (IsCharging)
        {
            double scale = (height - 2) / 13.0;
            double x = body.Left + (body.Width - 8.5 * scale) / 2;
            double y = body.Top + (body.Height - 13 * scale) / 2;
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
            dc.DrawGeometry(Brushes.White, BoltPen, Bolt);
            dc.Pop();
        }
    }
}
