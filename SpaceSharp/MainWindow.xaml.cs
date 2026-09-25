using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Security.Principal;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class MainWindow : Window
{
    private const int MaxVisibleCrumbs = 7;

    private readonly DiskScanner _scanner = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly Stopwatch _scanClock = new();

    private CancellationTokenSource? _scanCts;
    private FsNode? _root;
    private string? _lastScanPath;
    private FsNode? _breadcrumbFor;
    private readonly DispatcherTimer _filterTimer;
    private readonly DispatcherTimer _tipTimer;
    private FileFilter? _filter;
    private FilterResult? _filterResult;
    private TopListKind _listKind = TopListKind.Files;
    private Point _mousePosition;
    private FsNode? _tipNode;
    private long? _scanExpectedBytes;   // used space of the drive being scanned, for a real progress bar
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _applyingSettings;

    /// <summary>One drive in the side panel. Public properties for binding.</summary>
    public sealed class DriveRow
    {
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;      // "C:  Windows"
        public string FreeText { get; init; } = string.Empty;  // "231 GB free"
        public string Detail { get; init; } = string.Empty;    // "722 GB used of 953 GB · NTFS"
        public double Fraction { get; init; }                  // used share, 0..1
        public double BarWidth => Math.Round(Fraction * 300);
        public bool IsCurrent { get; init; }
    }

    public MainWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        Treemap.HoveredNodeChanged += (_, node) =>
        {
            ShowNodeInfo(node ?? Treemap.SelectedNode);
            ArmTooltip(node);
        };
        Treemap.SelectionChanged += (_, node) => ShowNodeInfo(node);
        Treemap.MouseMove += (_, e) => _mousePosition = e.GetPosition(Treemap);
        Treemap.MouseLeave += (_, _) => HideTooltip();
        Treemap.MouseWheel += (_, _) => HideTooltip();
        Treemap.MouseDown += (_, _) => HideTooltip();

        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };
        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer.Stop();
            ShowTooltip();
        };
        Treemap.NodeActivated += (_, node) => ZoomToFolder(node);
        Treemap.FocusChanged += (_, _) => UpdateNavigation();
        Treemap.ZoomChanged += (_, _) => UpdateZoomControls();

        PaletteCombo.ItemsSource = Palette.Schemes;
        ApplySettings();
        UpdateThemeButton();
        LoadDrives();
        UpdateNavigation();

        if (IsElevated) Title += "  (Administrator)";

        // Started with a folder argument (e.g. after "Restart as administrator"): scan it right away.
        if (App.StartupScanPath is { } startPath)
            Loaded += async (_, _) => await StartScanAsync(startPath);

        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    // =============================================================== updates

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates || !Updater.Instance.IsInstalled) return;
        await Task.Delay(TimeSpan.FromSeconds(4)); // let the window settle first
        if (await Updater.Instance.CheckAsync() is null) return;

        UpdateText.Text = $"SpaceSharp {Updater.Instance.AvailableVersion} is available. You're on {Updater.Instance.CurrentVersion}.";
        InstallUpdateButton.IsEnabled = true;
        UpdateBar.Visibility = Visibility.Visible;
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        InstallUpdateButton.IsEnabled = false;
        try
        {
            await Updater.Instance.InstallAndRestartAsync(percent =>
                Dispatcher.BeginInvoke(() => UpdateText.Text = $"Downloading update… {percent}%"));
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"The update could not be installed: {ex.Message}";
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void CloseUpdateBar_Click(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private bool IsScanning => _scanCts is not null;


    // ================================================================= setup

    /// <summary>Fills the Drives section of the side panel. Called at startup and after every scan.</summary>
    private void LoadDrives()
    {
        var rows = new List<DriveRow>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                string letter = drive.Name.TrimEnd('\\');
                string kind = drive.DriveType switch
                {
                    DriveType.Removable => "Removable",
                    DriveType.Network => "Network",
                    DriveType.CDRom => "Optical",
                    _ => drive.DriveFormat
                };
                rows.Add(new DriveRow
                {
                    Path = drive.RootDirectory.FullName,
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? letter : $"{letter}  {drive.VolumeLabel}",
                    FreeText = $"{SizeFormatter.Format(drive.AvailableFreeSpace)} free",
                    Detail = $"{SizeFormatter.Format(used)} used of {SizeFormatter.Format(drive.TotalSize)} · {kind}",
                    Fraction = drive.TotalSize > 0 ? (double)used / drive.TotalSize : 0,
                    IsCurrent = _lastScanPath is not null && string.Equals(_lastScanPath, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive vanished or is locked; skip it.
            }
        }
        DriveList.ItemsSource = rows;
    }

    private async void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveList.SelectedItem is not DriveRow row) return;
        DriveList.SelectedItem = null; // rows act like buttons
        if (!IsScanning) await StartScanAsync(row.Path);
    }

    /// <summary>Pushes every saved setting into the UI and the map. Called at startup and by the Settings window.</summary>
    public void ApplySettings()
    {
        var scheme = Palette.Find(_settings.Palette);
        var mode = _settings.ColorMode == nameof(ColorMode.ByFileType) ? ColorMode.ByFileType : ColorMode.ByDepth;

        _applyingSettings = true;
        PaletteCombo.SelectedItem = scheme;
        ColorCombo.SelectedIndex = mode == ColorMode.ByFileType ? 1 : 0;
        _applyingSettings = false;

        Treemap.Scheme = scheme;
        Treemap.ColorMode = mode;
        Treemap.SizeMode = _settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize;
        Treemap.Cushion = _settings.Cushion;
        var mapStyle = Enum.TryParse<MapStyle>(_settings.MapStyle, out var style) ? style : MapStyle.Classic;
        Treemap.MapStyle = mapStyle;
        Treemap.LabelScale = _settings.LabelSize switch { "Large" => 1.2, "Larger" => 1.4, _ => 1.0 };
        Treemap.LabelHalo = _settings.LabelHalo;

        _applyingSettings = true;
        StyleCombo.SelectedIndex = (int)mapStyle;
        _applyingSettings = false;
        Treemap.AnimateZoom = _settings.AnimateZoom;
        Treemap.MergeSingleFolderChains = _settings.MergeChains;
        Treemap.GroupSmallItems = _settings.GroupSmallItems;
        BuildLegend(scheme);
        LegendPanel.Visibility = mode == ColorMode.ByFileType ? Visibility.Visible : Visibility.Collapsed;

        if (_root is not null && _root.FreeSpaceVisible != _settings.ShowFreeSpace)
        {
            _root.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
            Treemap.Refresh();
        }

        SidePanel.Visibility = _settings.ShowSidePanel ? Visibility.Visible : Visibility.Collapsed;
        RefreshTopList();
        if (_filter is not null && !_filter.IsEmpty) ApplyFilter(); // size measure may have changed

        var theme = Enum.TryParse<AppTheme>(_settings.Theme, out var parsed) ? parsed : AppTheme.System;
        if (theme != ThemeManager.Choice) ThemeManager.Set(theme);

        _breadcrumbFor = null;
        UpdateNavigation();
        ShowNodeInfo(Treemap.SelectedNode);
        _settings.Save();
    }

    private void BuildLegend(ColorScheme scheme)
    {
        LegendPanel.Children.Clear();
        foreach (var (category, brush) in scheme.Categories)
        {
            LegendPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 0, 0),
                Children =
                {
                    new Border
                    {
                        Width = 11, Height = 11, Background = brush, CornerRadius = new CornerRadius(3),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Themed(new TextBlock
                    {
                        Text = category.ToString(), Margin = new Thickness(5, 0, 0, 0),
                        FontSize = 12, VerticalAlignment = VerticalAlignment.Center
                    }, TextBlock.ForegroundProperty, "TextDim")
                }
            });
        }
    }

    // ============================================================== scanning

    private async Task StartScanAsync(string path)
    {
        if (IsScanning) return;

        string? previousFocus = Treemap.FocusedFolder?.FullPath;
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        PrepareScanOverlay(path);
        SetScanning(true);
        _scanClock.Restart();
        _progressTimer.Start();

        try
        {
            var options = new ScanOptions
            {
                DetectHardLinks = _settings.DetectHardLinks,
                IncludeHidden = _settings.IncludeHidden
            };
            FsNode root = await _scanner.ScanAsync(path, options, cts.Token);
            root.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
            _root = root;
            _lastScanPath = path;
            Treemap.SelectedNode = null;
            Treemap.Root = root;
            ApplyFilter();
            RefreshTopList();
            LoadDrives();

            var progress = _scanner.GetProgress();
            StatusScan.Text = $"{root.FileCount:N0} files · {progress.Directories:N0} folders · " +
                              $"{SizeFormatter.Format(progress.Bytes)} · {_scanClock.Elapsed.TotalSeconds:0.0} s";
            ShowAccessBar(progress.DeniedFolders);

            // On a rescan, go back to the folder the user was looking at.
            if (previousFocus is not null && root.FindDescendant(previousFocus) is { } folder && folder != root)
                Treemap.FocusOn(folder, animate: false);
        }
        catch (OperationCanceledException)
        {
            StatusScan.Text = "Scan cancelled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _progressTimer.Stop();
            _scanClock.Stop();
            cts.Dispose();
            _scanCts = null;
            SetScanning(false);
        }
    }

    private void ShowAccessBar(long deniedFolders)
    {
        bool show = deniedFolders > 0 && !IsElevated;
        AccessBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            AccessText.Text = $"{deniedFolders:N0} protected {(deniedFolders == 1 ? "folder" : "folders")} couldn't be read, so the map is missing their contents.";
    }

    private void RestartElevated()
    {
        if (_lastScanPath is null || Environment.ProcessPath is not { } exe) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"\"{_lastScanPath}\"") { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The UAC prompt was cancelled; keep running as we are.
        }
    }

    private void SetScanning(bool scanning)
    {
        ScanOverlay.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        if (scanning) AccessBar.Visibility = Visibility.Collapsed;
        ScanFolderButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        if (!scanning) ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        UpdateNavigation();
    }

    private void PrepareScanOverlay(string path)
    {
        _scanExpectedBytes = null;
        string target = path;
        try
        {
            if (DiskScanner.IsDriveRoot(path))
            {
                var drive = new DriveInfo(path);
                _scanExpectedBytes = Math.Max(1, drive.TotalSize - drive.AvailableFreeSpace);
                target = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}";
            }
            else
            {
                target = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        ScanTitle.Text = $"Scanning {target}";
        ScanSubtitle.Text = _scanExpectedBytes is null
            ? "Reading the folder tree. The map appears when it's done."
            : $"Reading {SizeFormatter.Format(_scanExpectedBytes.Value)} of used space. The map appears when it's done.";
        ScanSizeText.Text = "0 B";
        ScanFilesText.Text = "0";
        ScanFoldersText.Text = "0";
        ScanRateText.Text = string.Empty;
        ScanElapsedText.Text = string.Empty;
        ScanPathText.Text = path;
        SetScanBar(_scanExpectedBytes is null ? null : 0);
    }

    private void UpdateProgress()
    {
        var p = _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);

        ScanSizeText.Text = SizeFormatter.Format(p.Bytes);
        ScanFilesText.Text = p.Files.ToString("N0");
        ScanFoldersText.Text = p.Directories.ToString("N0");
        ScanElapsedText.Text = _scanClock.Elapsed.ToString(@"m\:ss");
        ScanRateText.Text = p.Files / seconds >= 1000
            ? $"{p.Files / seconds / 1000:0.0}k files per second"
            : $"{p.Files / seconds:0} files per second";
        ScanPathText.Text = ShortenPath(p.CurrentPath, 90);

        if (_scanExpectedBytes is { } expected)
        {
            double fraction = Math.Clamp((double)p.Bytes / expected, 0, 0.99);
            SetScanBar(fraction);
            ScanTitle.Text = $"{ScanTitleBase()}  ·  {fraction * 100:0}%";
        }
    }

    private string ScanTitleBase()
    {
        int dot = ScanTitle.Text.IndexOf("  ·", StringComparison.Ordinal);
        return dot < 0 ? ScanTitle.Text : ScanTitle.Text[..dot];
    }

    /// <summary>Sets the bar to a fraction (0..1), or starts the sweeping animation when null.</summary>
    private void SetScanBar(double? fraction)
    {
        const double trackWidth = 564; // card width minus padding
        if (fraction is { } f)
        {
            ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            ScanBarSweep.X = 0;
            ScanBarFill.Width = Math.Max(6, trackWidth * f);
            return;
        }

        ScanBarFill.Width = 160;
        var sweep = new System.Windows.Media.Animation.DoubleAnimation(-160, trackWidth, TimeSpan.FromSeconds(1.4))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
    }

    /// <summary>Keeps the start and the end of a long path: "C:\Windows\…\amd64_microsoft-windows-…".</summary>
    private static string ShortenPath(string path, int maxLength)
    {
        if (path.Length <= maxLength) return path;
        var parts = path.Split('\\');
        if (parts.Length <= 3) return path;

        // Keep the first two segments and as many trailing segments as fit.
        var head = string.Join('\\', parts.Take(2));
        var tail = new List<string>();
        int length = head.Length + 4; // "\…\"
        for (int i = parts.Length - 1; i >= 2; i--)
        {
            if (length + parts[i].Length + 1 > maxLength && tail.Count > 0) break;
            tail.Insert(0, parts[i]);
            length += parts[i].Length + 1;
        }
        return $"{head}\\…\\{string.Join('\\', tail)}";
    }

    // ============================================================ navigation

    private void ZoomToFolder(FsNode node)
    {
        if (IsScanning) return;
        var folder = node.IsDirectory ? node : node.Parent;
        if (folder is not null)
            Treemap.FocusOn(folder);
    }

    private void GoUp()
    {
        if (IsScanning || Treemap.FocusedFolder is not { Parent: { } parent } current) return;
        Treemap.SelectedNode = current; // highlight where we came from
        Treemap.FocusOn(parent);
    }

    private void ShowAll()
    {
        if (!IsScanning) Treemap.ShowAll();
    }

    private void UpdateNavigation()
    {
        var focus = Treemap.FocusedFolder;
        UpButton.IsEnabled = !IsScanning && focus?.Parent is not null;
        RescanButton.IsEnabled = !IsScanning && _lastScanPath is not null;
        EmptyHint.Visibility = _root is null && !IsScanning ? Visibility.Visible : Visibility.Collapsed;
        UpdateBreadcrumb(focus);
        UpdateZoomControls();
    }

    private void UpdateZoomControls()
    {
        bool hasMap = Treemap.Root is not null && !IsScanning;
        double zoom = Treemap.Zoom;
        ZoomLabel.Text = !hasMap ? "—" : zoom < 100 ? $"{zoom * 100:0}%" : $"{zoom:N0}×";
        ZoomInButton.IsEnabled = hasMap && zoom < TreemapControl.MaxZoom;
        ZoomOutButton.IsEnabled = hasMap && zoom > 1.0001;
        FitButton.IsEnabled = hasMap && zoom > 1.0001;
    }

    /// <summary>Clickable path: every segment zooms the map to that folder.</summary>
    private void UpdateBreadcrumb(FsNode? focus)
    {
        if (ReferenceEquals(focus, _breadcrumbFor) && BreadcrumbPanel.Children.Count > 0) return;
        _breadcrumbFor = focus;
        BreadcrumbPanel.Children.Clear();

        if (focus is null)
        {
            BreadcrumbPanel.Children.Add(Themed(new TextBlock
            {
                Text = "No scan yet", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            }, TextBlock.ForegroundProperty, "TextDim"));
            BreadcrumbInfo.Text = string.Empty;
            return;
        }

        var chain = new List<FsNode>();
        for (var n = focus; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();

        // Long paths: root … last few folders (null marks the "…").
        var shown = new List<FsNode?>();
        if (chain.Count <= MaxVisibleCrumbs)
        {
            shown.AddRange(chain);
        }
        else
        {
            shown.Add(chain[0]);
            shown.Add(null);
            shown.AddRange(chain.Skip(chain.Count - (MaxVisibleCrumbs - 2)));
        }

        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0) BreadcrumbPanel.Children.Add(CrumbSeparator());

            if (shown[i] is not { } crumb)
            {
                BreadcrumbPanel.Children.Add(Themed(new TextBlock
                {
                    Text = "…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0)
                }, TextBlock.ForegroundProperty, "TextDim"));
                continue;
            }

            bool isLast = i == shown.Count - 1;
            var button = new Button
            {
                Style = (Style)FindResource("CrumbButton"),
                Content = crumb.Name,
                ToolTip = crumb.FullPath
            };
            if (i == 0) button.Tag = "\uEDA2";
            if (isLast)
            {
                button.SetResourceReference(ForegroundProperty, "Text");
                button.FontWeight = FontWeights.SemiBold;
            }
            button.Click += (_, _) => Treemap.FocusOn(crumb);
            BreadcrumbPanel.Children.Add(button);
        }

        BreadcrumbInfo.Text = $"{SizeFormatter.Format(focus.SizeFor(Treemap.SizeMode))} · {focus.FileCount:N0} files";
    }

    private TextBlock CrumbSeparator() => Themed(new TextBlock
    {
        Text = "\uE76C",
        FontFamily = (FontFamily)FindResource("IconFont"),
        FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 1, 2, 0)
    }, TextBlock.ForegroundProperty, "TextDim");

    /// <summary>Binds a property to a theme color so it follows light/dark switches.</summary>
    private static T Themed<T>(T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    // ================================================================= theme

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeButton();

    private void UpdateThemeButton()
    {
        ThemeButton.Tag = ThemeManager.IsDark ? "\uE708" : "\uE706"; // moon / sun
        ThemeButton.ToolTip = ThemeManager.Choice switch
        {
            AppTheme.Light => "Theme: Light",
            AppTheme.Dark => "Theme: Dark",
            _ => $"Theme: Match Windows ({(ThemeManager.IsDark ? "dark" : "light")})"
        };
    }

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => RestartElevated();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>Flips a boolean setting from a shortcut and confirms it in the status bar.</summary>
    private void ToggleSetting(Action<bool> set, bool current, string name)
    {
        set(!current);
        ApplySettings();
        StatusScan.Text = $"{name} {(current ? "off" : "on")}";
    }

    private void ShowSettings() => new SettingsWindow(this) { Owner = this }.ShowDialog();

    private void CloseAccessBar_Click(object sender, RoutedEventArgs e) => AccessBar.Visibility = Visibility.Collapsed;

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ThemeButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        var options = new (AppTheme Theme, string Label, string Glyph)[]
        {
            (AppTheme.System, "Match Windows", "\uE7F4"),
            (AppTheme.Light, "Light", "\uE706"),
            (AppTheme.Dark, "Dark", "\uE708")
        };

        foreach (var (theme, label, glyph) in options)
        {
            bool current = theme == ThemeManager.Choice;
            var item = new MenuItem
            {
                Header = label,
                Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("MenuIcon") },
                InputGestureText = current ? "\u2713" : string.Empty,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => ThemeManager.Set(theme);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void ShowNodeInfo(FsNode? node)
    {
        var measure = Treemap.SizeMode;
        var selection = Treemap.SelectedNodes;

        if (selection.Count > 1 && (node is null || selection.Contains(node)))
        {
            long total = selection.Sum(n => n.SizeFor(measure));
            int files = selection.Sum(n => n.FileCount);
            StatusHover.Text = $"{selection.Count:N0} items selected · {SizeFormatter.Format(total)} · {files:N0} files    Del moves them to the Recycle Bin";
            return;
        }

        if (node is null)
        {
            StatusHover.Text = string.Empty;
            return;
        }

        var text = new StringBuilder();

        if (node.IsGroup)
        {
            text.Append(node.Name).Append(" too small to show in ").Append(node.Parent?.FullPath ?? node.FullPath)
                .Append("    ").Append(SizeFormatter.Format(node.SizeFor(measure))).Append("    zoom in to see them");
        }
        else if (node.IsFreeSpace)
        {
            text.Append("Free space on ").Append(node.Parent?.FullPath ?? node.FullPath).Append("    ").Append(SizeFormatter.Format(node.Size));
        }
        else if (node.IsHardLinkDuplicate)
        {
            text.Append(node.FullPath).Append($"    hard link, {SizeFormatter.Format(node.LinkedSize)} already counted elsewhere");
        }
        else
        {
            text.Append(node.FullPath).Append("    ").Append(SizeFormatter.Format(node.SizeFor(measure)));
            long other = node.SizeFor(measure == SizeMeasure.FileSize ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize);
            if (other != node.SizeFor(measure))
                text.Append(measure == SizeMeasure.FileSize ? $" ({SizeFormatter.Format(other)} on disk)" : $" ({SizeFormatter.Format(other)} file size)");
        }

        if (Treemap.FocusedFolder is { } focus && focus.SizeFor(measure) > 0 && !ReferenceEquals(focus, node))
            text.Append($"  ({100.0 * node.SizeFor(measure) / focus.SizeFor(measure):0.#}% of {focus.Name})");
        if (node.IsDirectory)
            text.Append($"    {node.FileCount:N0} files");
        if (node.AccessDenied)
            text.Append("    ⚠ some content could not be read");
        StatusHover.Text = text.ToString();
    }

    // =============================================================== actions

    private void DeleteNode(FsNode node) => DeleteNodes(new[] { node });

    private void DeleteSelection() => DeleteNodes(Treemap.SelectedNodes.ToList());

    /// <summary>Moves items to the Recycle Bin in one shell operation and updates the map.</summary>
    private void DeleteNodes(IReadOnlyList<FsNode> nodes)
    {
        if (IsScanning) return;

        // Real items only, never the root, and no item whose parent is also being deleted.
        var candidates = nodes.Where(n => n.IsReal && n.Parent is not null && !ReferenceEquals(n, Treemap.Root)).ToList();
        var items = candidates.Where(n => !candidates.Any(other => !ReferenceEquals(other, n) && other.IsAncestorOf(n))).ToList();
        if (items.Count == 0) return;

        var measure = Treemap.SizeMode;
        long total = items.Sum(n => n.SizeFor(measure));
        string size = SizeFormatter.Format(total);

        if (_settings.ConfirmDelete)
        {
            string what = items.Count == 1
                ? (items[0].IsDirectory
                    ? $"the folder \"{items[0].Name}\" ({items[0].FileCount:N0} files, {size})"
                    : $"the file \"{items[0].Name}\" ({size})")
                : $"{items.Count:N0} items ({items.Sum(n => n.FileCount):N0} files, {size})";
            string list = items.Count == 1
                ? items[0].FullPath
                : string.Join("\n", items.Take(8).Select(n => n.FullPath)) + (items.Count > 8 ? $"\n… and {items.Count - 8:N0} more" : "");

            var answer = MessageBox.Show(this, $"Move {what} to the Recycle Bin?\n\n{list}",
                "Move to Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        var owner = new WindowInteropHelper(this).Handle;
        bool ok = RecycleBin.TrySend(items.Select(n => n.FullPath).ToList(), owner, out var error);

        // Remove whatever is actually gone, even after a partial failure.
        long freed = 0;
        int removed = 0;
        foreach (var node in items)
        {
            if (File.Exists(node.FullPath) || Directory.Exists(node.FullPath)) continue;
            freed += node.Allocated;
            node.RemoveFromTree(measure);
            removed++;
        }
        _root?.RegisterFreedSpace(freed, measure);

        Treemap.ClearSelection();
        Treemap.Refresh();
        ShowNodeInfo(null);
        _breadcrumbFor = null; // sizes changed
        UpdateNavigation();
        if (_filter is not null && !_filter.IsEmpty) ApplyFilter();
        RefreshTopList();
        LoadDrives();

        if (!ok)
        {
            MessageBox.Show(this, $"Not everything could be deleted.\n\n{error}", "Delete",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        StatusScan.Text = removed == 1 ? $"Moved {size} to the Recycle Bin" : $"Moved {removed:N0} items, {size}, to the Recycle Bin";
    }

    // ================================================================ filter

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearFilterButton.Visibility = FilterBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (FilterBox.Text.Length > 0) FilterBox.Clear();
                else Treemap.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                _filterTimer.Stop();
                ApplyFilter();
                Treemap.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        Treemap.Focus();
    }

    /// <summary>Evaluates the filter text over the whole tree and dims everything that doesn't match.</summary>
    private void ApplyFilter()
    {
        string text = FilterBox.Text.Trim();
        _filter = text.Length == 0 ? null : new FileFilter(FilterSpec.Parse(text), Treemap.SizeMode);

        if (_filter is null || _filter.IsEmpty || _root is null)
        {
            _filterResult = null;
            Treemap.SetFilterMatches(null);
            FilterInfo.Visibility = Visibility.Collapsed;
            SelectMatchesButton.Visibility = Visibility.Collapsed;
            return;
        }

        _filterResult = _filter.Evaluate(_root);
        Treemap.SetFilterMatches(_filterResult.Matches);
        FilterInfo.Text = _filterResult.FileCount == 0
            ? $"Nothing matches: {_filter.Description}"
            : $"{_filterResult.FileCount:N0} files · {SizeFormatter.Format(_filterResult.Bytes)}: {_filter.Description}";
        FilterInfo.Visibility = Visibility.Visible;
        SelectMatchesButton.Visibility = _filterResult.FileCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectMatches_Click(object sender, RoutedEventArgs e) => SelectMatches();

    /// <summary>Selects every matching file so one Del recycles them all.</summary>
    private void SelectMatches()
    {
        if (_filterResult is null || _filter is null) return;
        Treemap.SelectMany(_filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)));
        ShowNodeInfo(null);
    }

    // ============================================================= top lists

    private void Lists_Click(object sender, RoutedEventArgs e) => ToggleLists();

    private void ToggleLists()
    {
        _settings.ShowSidePanel = !_settings.ShowSidePanel;
        _settings.Save();
        SidePanel.Visibility = _settings.ShowSidePanel ? Visibility.Visible : Visibility.Collapsed;
        RefreshTopList();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        _listKind = ReferenceEquals(sender, TabFolders) ? TopListKind.Folders
                  : ReferenceEquals(sender, TabTypes) ? TopListKind.Types
                  : TopListKind.Files;
        RefreshTopList();
    }

    private void RefreshTopList()
    {
        if (SidePanel.Visibility != Visibility.Visible) return;

        foreach (var (button, kind) in new[] { (TabFiles, TopListKind.Files), (TabFolders, TopListKind.Folders), (TabTypes, TopListKind.Types) })
        {
            bool active = kind == _listKind;
            button.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) button.SetResourceReference(BackgroundProperty, "Control");
            else button.ClearValue(BackgroundProperty);
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (_root is null)
        {
            TopList.ItemsSource = null;
            ListHint.Text = "Scan a drive or folder to see the largest items.";
            return;
        }

        var rows = TopLists.Build(_root, _listKind, Treemap.SizeMode, Treemap.Scheme);
        TopList.ItemsSource = rows;
        ListHint.Text = _listKind switch
        {
            TopListKind.Files => $"Largest files in {_root.FullPath}",
            TopListKind.Folders => $"Largest folders in {_root.FullPath}",
            _ => $"Space by file type in {_root.FullPath}. Click a type to filter the map."
        };
    }

    private void TopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopList.SelectedItem is not TopRow row) return;
        TopList.SelectedItem = null; // rows act like buttons

        if (row.Node is { } node)
        {
            Treemap.SelectedNode = node;
            Treemap.FocusOn(node.IsDirectory ? node : node.Parent ?? node);
            ShowNodeInfo(node);
        }
        else if (row.FilterText is { } filter)
        {
            FilterBox.Text = filter;
        }
    }

    // =============================================================== tooltip

    private void ArmTooltip(FsNode? node)
    {
        _tipTimer.Stop();
        if (ReferenceEquals(node, _tipNode) && HoverTip.IsOpen) return;
        HoverTip.IsOpen = false;
        _tipNode = node;
        if (node is not null && _settings.ShowTooltips) _tipTimer.Start();
    }

    private void HideTooltip()
    {
        _tipTimer.Stop();
        HoverTip.IsOpen = false;
        _tipNode = null;
    }

    private void ShowTooltip()
    {
        var node = _tipNode;
        if (node is null || !Treemap.IsMouseOver) return;

        var measure = Treemap.SizeMode;
        TipName.Text = node.IsFreeSpace ? "Free space" : node.Name;
        TipPath.Text = node.IsGroup ? $"in {node.FullPath}" : node.IsFreeSpace ? node.Parent?.FullPath ?? "" : node.Parent?.FullPath ?? node.FullPath;

        TipRows.Children.Clear();
        TipRows.RowDefinitions.Clear();
        void Row(string label, string value)
        {
            int r = TipRows.RowDefinitions.Count;
            TipRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Themed(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 1, 14, 1) }, TextBlock.ForegroundProperty, "TextDim");
            var v = Themed(new TextBlock { Text = value, FontSize = 12, Margin = new Thickness(0, 1, 0, 1) }, TextBlock.ForegroundProperty, "Text");
            Grid.SetRow(l, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            TipRows.Children.Add(l); TipRows.Children.Add(v);
        }

        if (node.IsHardLinkDuplicate)
        {
            Row("Size", $"{SizeFormatter.Format(node.LinkedSize)} (hard link, counted elsewhere)");
        }
        else
        {
            Row("Size", SizeFormatter.Format(node.Size));
            if (node.Allocated != node.Size) Row("On disk", SizeFormatter.Format(node.Allocated));
        }
        if (node.IsDirectory || node.IsGroup) Row("Files", $"{node.FileCount:N0}");
        if (node.IsDirectory) Row("Folders", $"{node.Children.Count(c => c.IsDirectory):N0} directly inside");
        if (!node.IsDirectory && !node.IsFreeSpace && !node.IsGroup) Row("Type", DescribeType(node));
        if (node.LastWriteUtc > DateTime.MinValue) Row("Modified", DescribeDate(node.LastWriteUtc));
        if (Treemap.FocusedFolder is { } focus && focus.SizeFor(measure) > 0 && !ReferenceEquals(focus, node))
            Row("Share", $"{100.0 * node.SizeFor(measure) / focus.SizeFor(measure):0.#}% of {focus.Name}");
        if (node.AccessDenied) Row("Note", "Some content could not be read");

        HoverTip.HorizontalOffset = _mousePosition.X + 16;
        HoverTip.VerticalOffset = _mousePosition.Y + 20;
        HoverTip.IsOpen = true;
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Palette.Categorize(ext).ToString().ToLowerInvariant();
        return ext.Length == 0 ? $"no extension · {category}" : $"{ext} · {category}";
    }

    private static string DescribeDate(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var age = DateTime.UtcNow - utc;
        string ago = age.TotalDays < 1 ? "today"
                   : age.TotalDays < 2 ? "yesterday"
                   : age.TotalDays < 30 ? $"{(int)age.TotalDays} days ago"
                   : age.TotalDays < 365 ? $"{(int)(age.TotalDays / 30)} months ago"
                   : $"{age.TotalDays / 365:0.#} years ago";
        return $"{local:d}  ({ago})";
    }

    private void CopyPath(FsNode node) => RunSafely(() => Clipboard.SetText(node.FullPath));

    private void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SpaceSharp", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ======================================================== event handlers

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            await StartScanAsync(dialog.FolderName);
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScanPath is not null)
            await StartScanAsync(_lastScanPath);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void About_Click(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout() => new AboutWindow { Owner = this }.ShowDialog();

    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();

    private void Fit_Click(object sender, RoutedEventArgs e) => ShowAll();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Treemap.ZoomBy(2);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Treemap.ZoomBy(0.5);

    private void ColorSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Also fires during InitializeComponent, before everything exists.
        if (_applyingSettings || Treemap is null || LegendPanel is null || PaletteCombo is null || StyleCombo is null) return;

        _settings.Palette = (PaletteCombo.SelectedItem as ColorScheme ?? Palette.Default).Name;
        _settings.ColorMode = (ColorCombo.SelectedIndex == 1 ? ColorMode.ByFileType : ColorMode.ByDepth).ToString();
        if (StyleCombo.SelectedIndex >= 0) _settings.MapStyle = Enum.GetNames<MapStyle>()[StyleCombo.SelectedIndex];
        ApplySettings();
    }

    private void TreemapMenu_Opened(object sender, RoutedEventArgs e)
    {
        var node = Treemap.SelectedNode;
        int count = Treemap.SelectedNodes.Count;
        bool hasNode = node is not null && node.IsReal && !IsScanning;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;

        MenuFocus.IsEnabled = node is not null && !node.IsFreeSpace && !IsScanning && folder is not null && count <= 1;
        MenuUp.IsEnabled = !IsScanning && Treemap.FocusedFolder?.Parent is not null;
        MenuFit.IsEnabled = !IsScanning && Treemap.Root is not null && Treemap.Zoom > 1.0001;
        MenuOpen.IsEnabled = hasNode && count <= 1;
        MenuExplorer.IsEnabled = hasNode && count <= 1;
        MenuCopy.IsEnabled = hasNode;
        MenuDelete.IsEnabled = !IsScanning && Treemap.SelectedNodes.Any(n => n.IsReal && n.Parent is not null && !ReferenceEquals(n, Treemap.Root));
        MenuDelete.Header = count > 1 ? $"Move {count:N0} items to Recycle Bin" : "Move to Recycle Bin";
    }

    private void MenuFocus_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { } node) ZoomToFolder(node);
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is not { } node) return;
        RunSafely(() => Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true }));
    }

    private void MenuExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is not { } node) return;
        RunSafely(() => Process.Start("explorer.exe", $"/select,\"{node.FullPath}\""));
    }

    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelectedPaths();

    private void CopySelectedPaths()
    {
        var paths = Treemap.SelectedNodes.Where(n => n.IsReal).Select(n => n.FullPath).ToList();
        if (paths.Count > 0) RunSafely(() => Clipboard.SetText(string.Join(Environment.NewLine, paths)));
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FilterBox.IsKeyboardFocusWithin) return; // the box handles its own keys
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (e.Key)
        {
            case Key.F1:
                ShowAbout();
                break;
            case Key.OemComma when ctrl:
                ShowSettings();
                break;
            case Key.Escape when IsScanning:
                _scanCts?.Cancel();
                break;
            case Key.F5 when !IsScanning && _lastScanPath is not null:
                Rescan_Click(sender, e);
                break;
            case Key.Back:
                GoUp();
                break;
            case Key.Home:
            case Key.D0 or Key.NumPad0 when ctrl:
                ShowAll();
                break;
            case Key.OemPlus or Key.Add when !IsScanning:
                Treemap.ZoomBy(2);
                break;
            case Key.OemMinus or Key.Subtract when !IsScanning:
                Treemap.ZoomBy(0.5);
                break;
            case Key.Enter when Treemap.SelectedNode is { } zoomNode:
                ZoomToFolder(zoomNode);
                break;
            case Key.Delete when Treemap.SelectedNodes.Count > 0:
                DeleteSelection();
                break;
            case Key.C when ctrl && Treemap.SelectedNodes.Count > 0:
                CopySelectedPaths();
                break;
            case Key.F when ctrl:
                FilterBox.Focus();
                FilterBox.SelectAll();
                break;
            case Key.A when ctrl && _filterResult is not null:
                SelectMatches();
                break;
            case Key.L when !ctrl:
                ToggleLists();
                break;
            case Key.C when !ctrl:
                ToggleSetting(v => _settings.Cushion = v, _settings.Cushion, "Cushion shading");
                break;
            case Key.S when !ctrl:
            {
                var names = Enum.GetNames<MapStyle>();
                int index = Math.Max(0, Array.IndexOf(names, _settings.MapStyle ?? "Classic"));
                _settings.MapStyle = names[(index + 1) % names.Length];
                ApplySettings();
                StatusScan.Text = $"Map style: {_settings.MapStyle}";
                break;
            }
            case Key.G when !ctrl:
                ToggleSetting(v => _settings.GroupSmallItems = v, _settings.GroupSmallItems, "Grouping of small items");
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            GoUp();
            e.Handled = true;
        }
    }
}
