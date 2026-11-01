using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ZeiLand.Services;
using ZeiLand.Settings;

namespace ZeiLand;

public enum SettingsPage
{
    Island,
    Appearance,
    Aod,
    Shortcuts,
    Behaviour,
    About,
}

// The tabs, the appearance page (resting style, position, GIF), the shortcuts page and the about page
// (updates, guide, license, reset).
public partial class SettingsWindow
{
    private const string WebsiteGlyph = "";
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private List<AppChoice>? _apps;
    private Task<List<InstalledApp>>? _installedApps;
    private Dictionary<string, string>? _appNames; // app id -> name in the Start menu
    private string? _shownGif = ""; // the GIF in the preview; "" until the first one is loaded
    private DispatcherTimer? _resetDisarm;

    /// <summary>An installed app in the picker; its icon is only loaded once its row scrolls into view.</summary>
    public sealed class AppChoice(string name, string id)
    {
        private ImageSource? _icon;
        private bool _iconLoaded;

        public string Name { get; } = name;
        public string Id { get; } = id;

        public ImageSource? Icon
        {
            get
            {
                if (!_iconLoaded) _icon = AppIcons.ForAppId(Id, 32);
                _iconLoaded = true;
                return _icon;
            }
        }
    }

    public void ShowPage(SettingsPage page)
    {
        foreach (var tab in Tabs.Children.OfType<RadioButton>())
            if ((string)tab.Tag == page.ToString()) tab.IsChecked = true;
    }

    private void InitializeTabs()
    {
        var pages = new Dictionary<string, FrameworkElement>
        {
            [nameof(SettingsPage.Island)] = IslandPage,
            [nameof(SettingsPage.Appearance)] = AppearancePage,
            [nameof(SettingsPage.Aod)] = AodPage,
            [nameof(SettingsPage.Shortcuts)] = ShortcutsPage,
            [nameof(SettingsPage.Behaviour)] = BehaviourPage,
            [nameof(SettingsPage.About)] = AboutPage,
        };
        foreach (var tab in Tabs.Children.OfType<RadioButton>())
        {
            tab.Checked += (_, _) =>
            {
                foreach (var (name, page) in pages)
                    page.Visibility = name == (string)tab.Tag ? Visibility.Visible : Visibility.Hidden;
            };
        }
        ShowPage(SettingsPage.Island);
    }

    /// <summary>The Start menu's apps, read once per window (for the app picker and the players' names).</summary>
    private Task<List<InstalledApp>> InstalledApps() => _installedApps ??= Shortcuts.GetInstalledAppsAsync();

    // ---- Media sources -----------------------------------------------------------------------

    private void InitializeMediaSources()
    {
        RenderMediaSources();
        _media.SessionsChanged += RenderMediaSources;
        Closed += (_, _) => _media.SessionsChanged -= RenderMediaSources;
        _ = NameMediaSourcesAsync();
    }

    private async Task NameMediaSourcesAsync()
    {
        var apps = await InstalledApps();
        _appNames = apps.ToDictionary(app => app.Id, app => app.Name, StringComparer.OrdinalIgnoreCase);
        RenderMediaSources();
    }

    /// <summary>Every player that has a session now, plus the hidden ones: a chip each, ticked when shown.</summary>
    private void RenderMediaSources()
    {
        var hidden = _settings.HiddenMediaApps;
        var apps = _media.GetSessionApps().Concat(hidden).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        MediaSources.Children.Clear();
        foreach (var id in apps)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            if (AppIcons.ForAppId(id, 32) is { } icon)
            {
                var image = new Image { Source = icon, Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                label.Children.Add(image);
            }
            label.Children.Add(new TextBlock { Text = MediaAppName(id), VerticalAlignment = VerticalAlignment.Center });
            var chip = new CheckBox
            {
                Style = (Style)FindResource("Chip"),
                Content = label,
                ToolTip = id,
                IsChecked = !hidden.Contains(id, StringComparer.OrdinalIgnoreCase),
            };
            chip.Click += (_, _) => SetMediaAppHidden(id, chip.IsChecked != true);
            MediaSources.Children.Add(chip);
        }
        MediaSourcesEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The name in the Start menu; otherwise the id without ".exe" or the package's prefix.</summary>
    private string MediaAppName(string id)
    {
        if (_appNames is { } names)
        {
            if (names.TryGetValue(id, out var name)) return name;
            // Desktop apps report their exe; the Start menu knows them by path.
            var byPath = names.FirstOrDefault(entry => entry.Key.EndsWith("\\" + id, StringComparison.OrdinalIgnoreCase));
            if (byPath.Value is { } pathName) return pathName;
        }
        var bare = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
        int bang = bare.IndexOf('!');
        return bang >= 0 ? bare[(bang + 1)..] : bare;
    }

    private void SetMediaAppHidden(string id, bool hide)
    {
        var list = _settings.HiddenMediaApps.Where(app => !app.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hide) list.Add(id);
        _settings.HiddenMediaApps = list;
    }

    // ---- Always on display -------------------------------------------------------------------

    private void InitializeAod(Action previewAod)
    {
        foreach (var option in AodWhenOptions.Children.OfType<RadioButton>())
            option.Checked += (_, _) => _settings.AodTrigger = Enum.Parse<AodTrigger>((string)option.Tag);
        foreach (var option in AodWaitOptions.Children.OfType<RadioButton>())
            option.Checked += (_, _) => _settings.AodIdleMinutes = int.Parse((string)option.Tag);
        AodPreviewButton.Click += (_, _) => previewAod();
        RenderAod();
    }

    private void RenderAod()
    {
        foreach (var option in AodWhenOptions.Children.OfType<RadioButton>())
            option.IsChecked = (string)option.Tag == _settings.AodTrigger.ToString();
        foreach (var option in AodWaitOptions.Children.OfType<RadioButton>())
        {
            int minutes = int.Parse((string)option.Tag);
            option.Content = Loc.F("Minutes", minutes);
            option.IsChecked = minutes == _settings.AodIdleMinutes;
        }
        AodWaitRow.IsEnabled = _settings.AodTrigger == AodTrigger.Idle;

        // The island's screen, each screen, all of them; with a single screen there's nothing to choose.
        var displays = Displays.GetAll();
        AodScreenRow.Visibility = displays.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var choices = new List<(string? Value, string Label)> { (null, Loc.T("AodIslandScreen")) };
        choices.AddRange(displays.OrderBy(d => d.Number).Select(d => ((string?)d.Device, Loc.F("ScreenNumber", d.Number))));
        choices.Add(("*", Loc.T("AodAllScreens")));
        AodScreens.Children.Clear();
        foreach (var (value, label) in choices)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "AodScreen",
                Content = label,
                IsChecked = value == _settings.AodScreen,
            };
            option.Checked += (_, _) => _settings.AodScreen = value;
            AodScreens.Children.Add(option);
        }
    }

    // ---- Appearance --------------------------------------------------------------------------

    private void RenderSkip()
    {
        foreach (var option in SkipOptions.Children.OfType<RadioButton>())
        {
            int seconds = int.Parse((string)option.Tag);
            option.Content = seconds == 0 ? Loc.T("Off") : Loc.F("Seconds", seconds);
            option.IsChecked = seconds == _settings.SkipSeconds;
        }
    }

    private void RenderIslandSize()
    {
        foreach (var option in SizeOptions.Children.OfType<RadioButton>())
        {
            double scale = double.Parse((string)option.Tag, CultureInfo.InvariantCulture);
            option.Content = Loc.Percent(scale);
            option.IsChecked = scale == _settings.IslandScale;
        }
    }

    private void InitializeAppearance()
    {
        foreach (var option in SkipOptions.Children.OfType<RadioButton>())
            option.Checked += (_, _) => _settings.SkipSeconds = int.Parse((string)option.Tag);
        RenderSkip();
        foreach (var option in SizeOptions.Children.OfType<RadioButton>())
            option.Checked += (_, _) => _settings.IslandScale = double.Parse((string)option.Tag, CultureInfo.InvariantCulture);
        RenderIslandSize();

        UpdateRestingStyle();
        RestingThin.Checked += (_, _) => _settings.YieldToTopWindows = true;
        RestingPlayer.Checked += (_, _) => _settings.YieldToTopWindows = false;

        UpdatePosition();
        CentreButton.Click += (_, _) =>
        {
            _settings.IslandMonitor = null;
            _settings.IslandPosition = 0.5;
        };

        _ = ShowGif();
        GifPickButton.Click += async (_, _) => await PickGif();
        GifRemoveButton.Click += (_, _) => RemoveGif();
    }

    private void UpdateRestingStyle()
    {
        RestingThin.IsChecked = _settings.YieldToTopWindows;
        RestingPlayer.IsChecked = !_settings.YieldToTopWindows;
    }

    private void UpdatePosition()
    {
        // \\.\DISPLAY2 -> "Ekran 2"
        string monitor = _settings.IslandMonitor is { } device
            ? Loc.F("ScreenNumber", device.TrimStart('\\', '.').Replace("DISPLAY", "", StringComparison.OrdinalIgnoreCase))
            : Loc.T("MainScreen");
        double position = _settings.IslandPosition;
        PositionTitle.Text = Loc.F(position == 0.5 ? "PlaceCentred" : position < 0.5 ? "PlaceLeft" : "PlaceRight", monitor);
        CentreButton.IsEnabled = _settings.IslandMonitor is not null || position != 0.5;
    }

    private async Task ShowGif()
    {
        var path = _settings.GifPath;
        if (path != _shownGif)
        {
            _shownGif = path;
            await GifPreview.LoadAsync(path, IslandWindow.GifSlot.Width, IslandWindow.GifSlot.Height, IslandWindow.GifSlotRadius);
        }
        GifPlaceholder.Visibility = GifPreview.HasAnimation ? Visibility.Collapsed : Visibility.Visible;
        GifRemoveButton.IsEnabled = path is not null;
        GifDetail.Text = path is null
            ? Loc.T("GifTip")
            : Loc.T(GifPreview.HasAnimation ? "GifPlaying" : "GifMissing");
    }

    /// <summary>A copy is kept with the settings, so the original can be moved or deleted.</summary>
    private async Task PickGif()
    {
        var dialog = new OpenFileDialog { Title = Loc.T("PickGif"), Filter = "GIF|*.gif" };
        if (dialog.ShowDialog(this) != true) return;

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeiLand", "gif");
        var copy = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}.gif");
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(dialog.FileName, copy, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GifDetail.Text = Loc.T("GifCopyFailed");
            return;
        }

        GifPickButton.IsEnabled = false;
        GifDetail.Text = Loc.T("Preparing");
        _shownGif = copy;
        bool shown = await GifPreview.LoadAsync(copy, IslandWindow.GifSlot.Width, IslandWindow.GifSlot.Height, IslandWindow.GifSlotRadius);
        GifPickButton.IsEnabled = true;
        if (!shown)
        {
            Delete(copy);
            _shownGif = "";
            await ShowGif(); // the previous one again
            GifDetail.Text = Loc.T("GifFailed");
            return;
        }

        var previous = _settings.GifPath;
        _settings.GifPath = copy;
        if (previous is not null) Delete(previous); // the island keeps its frames in memory, not the file
    }

    private void RemoveGif()
    {
        if (_settings.GifPath is not { } path) return;
        _settings.GifPath = null;
        Delete(path);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- Shortcuts ---------------------------------------------------------------------------

    private void InitializeShortcuts()
    {
        RenderShortcuts();
        AddAppButton.Click += async (_, _) => await ToggleAppPicker();
        AddFileButton.Click += (_, _) => AddFile();
        AddFolderButton.Click += (_, _) => AddFolder();
        AddWebsiteButton.Click += (_, _) =>
        {
            AppPicker.Visibility = Visibility.Collapsed;
            WebsiteForm.Visibility = WebsiteForm.IsVisible ? Visibility.Collapsed : Visibility.Visible;
            if (WebsiteForm.IsVisible) WebsiteAddress.Focus();
        };
        WebsiteAddButton.Click += (_, _) => AddWebsite();
        WebsiteAddress.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) AddWebsite();
        };
        WebsiteName.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) AddWebsite();
        };
        AppSearch.TextChanged += (_, _) => FilterApps();
        AppList.MouseLeftButtonUp += (_, _) =>
        {
            if (AppList.SelectedItem is not AppChoice app) return;
            if (Add(new ShortcutItem(ShortcutKind.App, app.Id, app.Name))) AppPicker.Visibility = Visibility.Collapsed;
            AppList.SelectedItem = null;
        };
    }

    private void RenderShortcuts()
    {
        var shortcuts = _settings.Shortcuts;
        ShortcutList.Children.Clear();
        if (shortcuts.Count == 0)
        {
            ShortcutList.Children.Add(new TextBlock
            {
                Text = Loc.T("NoShortcuts"),
                Margin = new Thickness(16, 10, 16, 10),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryBrush,
            });
        }
        for (int i = 0; i < shortcuts.Count; i++) ShortcutList.Children.Add(CreateShortcutRow(shortcuts[i], i, shortcuts.Count));

        bool full = shortcuts.Count >= AppSettings.MaxShortcuts;
        AddAppButton.IsEnabled = AddFileButton.IsEnabled = AddFolderButton.IsEnabled = AddWebsiteButton.IsEnabled = !full;
        if (full)
        {
            AppPicker.Visibility = WebsiteForm.Visibility = Visibility.Collapsed;
            ShortcutStatus.Text = Loc.T("ShortcutsFull");
        }
    }

    private Grid CreateShortcutRow(ShortcutItem shortcut, int index, int count)
    {
        var row = new Grid { Margin = new Thickness(16, 6, 10, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement icon = Shortcuts.Icon(shortcut, 40) is { } image
            ? new Image { Source = image, Width = 20, Height = 20 }
            : new TextBlock { Text = WebsiteGlyph, FontFamily = IconFont, FontSize = 16, Width = 20, TextAlignment = TextAlignment.Center };
        icon.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        row.Children.Add(icon);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = shortcut.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = shortcut.Kind switch
            {
                ShortcutKind.App => Loc.T("App"),
                ShortcutKind.Website => shortcut.Target,
                _ => shortcut.Target,
            },
            FontSize = 11,
            Foreground = SecondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = shortcut.Target,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(RowButton("", Loc.T("MoveUp"), index > 0, () => Move(index, -1)));
        buttons.Children.Add(RowButton("", Loc.T("MoveDown"), index < count - 1, () => Move(index, +1)));
        buttons.Children.Add(RowButton("", Loc.T("Remove"), true, () => Remove(index)));
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);
        return row;
    }

    private Button RowButton(string glyph, string toolTip, bool enabled, Action click)
    {
        var button = new Button { Style = (Style)FindResource("RowIcon"), Content = glyph, ToolTip = toolTip, IsEnabled = enabled };
        button.Click += (_, _) => click();
        return button;
    }

    private void Move(int index, int offset)
    {
        var list = _settings.Shortcuts.ToList();
        (list[index], list[index + offset]) = (list[index + offset], list[index]);
        _settings.Shortcuts = list;
    }

    private void Remove(int index)
    {
        var list = _settings.Shortcuts.ToList();
        list.RemoveAt(index);
        _settings.Shortcuts = list;
        ShortcutStatus.Text = "";
    }

    /// <summary>Adds it to the end. False (with a message) if it's already there or the list is full.</summary>
    private bool Add(ShortcutItem shortcut)
    {
        var list = _settings.Shortcuts;
        if (list.Count >= AppSettings.MaxShortcuts) return false;
        if (list.Any(s => s.Kind == shortcut.Kind && s.Target.Equals(shortcut.Target, StringComparison.OrdinalIgnoreCase)))
        {
            ShortcutStatus.Text = Loc.F("AlreadyAdded", shortcut.Name);
            return false;
        }
        _settings.Shortcuts = [.. list, shortcut];
        ShortcutStatus.Text = Loc.F("Added", shortcut.Name);
        return true;
    }

    private async Task ToggleAppPicker()
    {
        WebsiteForm.Visibility = Visibility.Collapsed;
        if (AppPicker.IsVisible)
        {
            AppPicker.Visibility = Visibility.Collapsed;
            return;
        }
        AppPicker.Visibility = Visibility.Visible;
        AppSearch.Text = "";
        AppSearch.Focus();
        if (_apps is null)
        {
            AppPickerStatus.Text = Loc.T("LoadingApps");
            var apps = await InstalledApps();
            _apps = apps.Select(app => new AppChoice(app.Name, app.Id)).ToList();
        }
        FilterApps();
    }

    private void FilterApps()
    {
        if (_apps is null) return;
        var query = AppSearch.Text.Trim();
        var matches = query.Length == 0
            ? _apps
            : _apps.Where(app => app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        AppList.ItemsSource = matches;
        AppPickerStatus.Text = Loc.T(matches.Count == 0 ? "NoMatchingApps" : "ClickToAdd");
    }

    private void AddFile()
    {
        AppPicker.Visibility = WebsiteForm.Visibility = Visibility.Collapsed;
        var dialog = new OpenFileDialog { Title = Loc.T("PickFile"), Filter = Loc.T("AllFiles") + "|*.*", DereferenceLinks = false };
        if (dialog.ShowDialog(this) != true) return;
        Add(new ShortcutItem(ShortcutKind.File, dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName)));
    }

    private void AddFolder()
    {
        AppPicker.Visibility = WebsiteForm.Visibility = Visibility.Collapsed;
        var dialog = new OpenFolderDialog { Title = Loc.T("PickFolder") };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FolderName;
        var name = Path.GetFileName(path.TrimEnd('\\'));
        Add(new ShortcutItem(ShortcutKind.Folder, path, name.Length > 0 ? name : path));
    }

    private void AddWebsite()
    {
        var address = WebsiteAddress.Text.Trim();
        if (address.Length == 0) return;
        if (!address.Contains("://")) address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
        {
            ShortcutStatus.Text = Loc.T("NotAnAddress");
            return;
        }
        var name = WebsiteName.Text.Trim();
        if (name.Length == 0) name = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (!Add(new ShortcutItem(ShortcutKind.Website, uri.AbsoluteUri, name))) return;
        WebsiteAddress.Text = WebsiteName.Text = "";
        WebsiteForm.Visibility = Visibility.Collapsed;
    }

    // ---- About -------------------------------------------------------------------------------

    private void InitializeAbout(Action showWelcome)
    {
        AboutTitle.Text = $"ZeiLand {UpdateService.CurrentVersion}";
        VersionText.Text = Loc.T("SettingsSaved");
        UpdateUpdateStatus();
        _updates.Changed += UpdateUpdateStatus;
        UpdateButton.Click += async (_, _) => await _updates.CheckAsync(installNow: _updates.State == UpdateState.Ready);

        WelcomeButton.Click += (_, _) => showWelcome();
        LicenseButton.Click += (_, _) =>
        {
            // Installed next to the app; in a development build, the copy on GitHub.
            var license = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            Open(File.Exists(license) ? license : $"https://github.com/{UpdateService.Repository}/blob/main/LICENSE");
        };
        ReleasesButton.Click += (_, _) => Open(UpdateService.ReleasesPage);
        ResetButton.Click += (_, _) => OnResetClick();
    }

    private void UpdateUpdateStatus()
    {
        string checkedAt = _updates.LastChecked is { } time ? Loc.F("LastChecked", time) : "";
        (string title, string detail, string button, bool enabled) = _updates.State switch
        {
            UpdateState.Checking => (Loc.T("Checking"), "", Loc.T("CheckNow"), false),
            UpdateState.Downloading => (Loc.F("Downloading", _updates.LatestVersion), "", Loc.T("CheckNow"), false),
            UpdateState.Ready => (Loc.F("UpdateReady", _updates.LatestVersion),
                _updates.Error ?? Loc.T(_settings.AutoUpdate ? "WillInstallItself" : "PressInstall"),
                Loc.T("Install"), true),
            UpdateState.Installing => (Loc.F("InstallingUpdate", _updates.LatestVersion), Loc.T("ComesBackSoon"), Loc.T("Install"), false),
            UpdateState.UpToDate => (Loc.T("UpToDate"), checkedAt, Loc.T("CheckNow"), true),
            UpdateState.Failed => (Loc.T("CheckFailed"), $"{_updates.Error}. {checkedAt}".Trim(), Loc.T("TryAgain"), true),
            _ => (Loc.F("VersionNumber", UpdateService.CurrentVersion), Loc.T("UpdatesFromGitHub"), Loc.T("CheckNow"), true),
        };
        UpdateTitle.Text = title;
        UpdateDetail.Text = detail;
        UpdateDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Content = button;
        UpdateButton.IsEnabled = enabled;
    }

    /// <summary>Everything back to defaults except the shortcuts; a second click within 3 s confirms.</summary>
    private void OnResetClick()
    {
        if (_resetDisarm is null)
        {
            ResetButton.Content = Loc.T("ResetConfirm");
            _resetDisarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _resetDisarm.Tick += (_, _) => DisarmReset();
            _resetDisarm.Start();
            return;
        }
        DisarmReset();
        _settings.ResetToDefaults();
        foreach (var option in HoverDelayOptions.Children.OfType<RadioButton>())
            option.IsChecked = int.Parse((string)option.Tag) == _settings.HoverDelayMs;
        foreach (var option in WheelStepOptions.Children.OfType<RadioButton>())
            option.IsChecked = int.Parse((string)option.Tag) == _settings.WheelStepPercent;
        UpdateShortcut();
        VersionText.Text = Loc.T("ResetDone");
    }

    private void DisarmReset()
    {
        _resetDisarm?.Stop();
        _resetDisarm = null;
        ResetButton.SetResourceReference(ContentProperty, "ResetSettings");
    }

    private static void Open(string target)
    {
        try
        {
            // Through Explorer, so even from the elevated island it opens as the signed-in user.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
