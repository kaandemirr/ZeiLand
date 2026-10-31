using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ZeiLand.Services;
using ZeiLand.Settings;

namespace ZeiLand.Controls;

/// <summary>CPU / GPU / RAM row at the bottom of the expanded island, plus the optimize button.</summary>
public partial class StatsPanel : UserControl
{
    private const string Placeholder = "–";

    private static readonly Brush SecondaryBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)).Frozen();
    private static readonly Brush WarmBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)).Frozen();
    private static readonly Brush HotBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).Frozen();

    private bool _showCpuTemperature = true;
    private bool _showGpuTemperature = true;

    public StatsPanel()
    {
        InitializeComponent();
        OptimizeButton.Click += (_, _) => OptimizeRequested?.Invoke();
        Clear();
    }

    public event Action? OptimizeRequested;

    /// <summary>Shows only the readings the user picked in settings.</summary>
    internal void Configure(AppSettings settings, bool separated)
    {
        CpuColumn.Visibility = settings.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
        GpuColumn.Visibility = settings.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
        RamColumn.Visibility = settings.ShowRam ? Visibility.Visible : Visibility.Collapsed;
        _showCpuTemperature = settings.ShowCpuTemperature;
        _showGpuTemperature = settings.ShowGpuTemperature;

        // A divider only makes sense when something sits above the row.
        BorderThickness = new Thickness(0, separated ? 1 : 0, 0, 0);
        Padding = new Thickness(0, separated ? 10 : 0, 0, 0);
    }

    public void Clear()
    {
        CpuValue.Text = GpuValue.Text = RamValue.Text = Placeholder;
        CpuTemperature.Text = GpuTemperature.Text = RamUsed.Text = "";
        CpuScale.ScaleX = GpuScale.ScaleX = RamScale.ScaleX = 0;
    }

    internal void Show(SystemStats stats)
    {
        CpuValue.Text = Percent(stats.CpuUsage);
        CpuScale.ScaleX = stats.CpuUsage;
        ShowTemperature(CpuTemperature, _showCpuTemperature ? stats.CpuTemperature : null, warm: 80, hot: 90);

        GpuValue.Text = stats.GpuUsage is { } gpu ? Percent(gpu) : Placeholder;
        GpuScale.ScaleX = stats.GpuUsage ?? 0;
        ShowTemperature(GpuTemperature, _showGpuTemperature ? stats.GpuTemperature : null, warm: 80, hot: 90);

        RamValue.Text = Percent(stats.RamUsage);
        RamUsed.Text = $" · {stats.RamUsedGb.ToString("0.0", Loc.Culture)} GB";
        RamScale.ScaleX = stats.RamUsage;
    }

    private static void ShowTemperature(Run run, double? temperature, double warm, double hot)
    {
        if (temperature is not { } value)
        {
            run.Text = "";
            return;
        }
        run.Text = $" · {value:0}°";
        run.Foreground = value >= hot ? HotBrush : value >= warm ? WarmBrush : SecondaryBrush;
    }

    private static string Percent(double fraction) => Loc.Percent(fraction);
}
