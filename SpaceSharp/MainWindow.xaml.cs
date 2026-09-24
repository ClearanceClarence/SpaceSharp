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
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _applyingSettings;

    private sealed record DriveEntry(string Path, string Label)
    {
        public override string ToString() => Label;
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

        Treemap.HoveredNodeChanged += (_, node) => ShowNodeInfo(node ?? Treemap.SelectedNode);
        Treemap.SelectionChanged += (_, node) => ShowNodeInfo(node);
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
    }

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

    private void LoadDrives()
    {
        DriveCombo.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                string name = drive.Name.TrimEnd('\\');
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? name : $"{name}  {drive.VolumeLabel}";
                DriveCombo.Items.Add(new DriveEntry(drive.RootDirectory.FullName,
                    $"{label} — {SizeFormatter.Format(drive.AvailableFreeSpace)} free of {SizeFormatter.Format(drive.TotalSize)}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive vanished or is locked; skip it.
            }
        }

        if (DriveCombo.Items.Count > 0)
            DriveCombo.SelectedIndex = 0;
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
        ScanDriveButton.IsEnabled = !scanning;
        ScanFolderButton.IsEnabled = !scanning;
        DriveCombo.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        if (scanning)
        {
            ScanProgressText.Text = "Starting…";
            ScanPathText.Text = string.Empty;
        }
        UpdateNavigation();
    }

    private void UpdateProgress()
    {
        var p = _scanner.GetProgress();
        ScanProgressText.Text = $"{p.Files:N0} files · {p.Directories:N0} folders · " +
                                $"{SizeFormatter.Format(p.Bytes)} · {_scanClock.Elapsed.ToString(@"mm\:ss")}";
        ScanPathText.Text = p.CurrentPath;
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
        if (node is null)
        {
            StatusHover.Text = string.Empty;
            return;
        }

        var measure = Treemap.SizeMode;
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

    private void DeleteNode(FsNode node)
    {
        if (IsScanning || node.Parent is null || !node.IsReal || ReferenceEquals(node, Treemap.Root)) return;

        string size = SizeFormatter.Format(node.SizeFor(Treemap.SizeMode));
        string description = node.IsDirectory
            ? $"folder \"{node.Name}\" ({node.FileCount:N0} files, {size})"
            : $"file \"{node.Name}\" ({size})";

        if (_settings.ConfirmDelete)
        {
            var answer = MessageBox.Show(this, $"Move the {description} to the Recycle Bin?\n\n{node.FullPath}",
                "Move to Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        var owner = new WindowInteropHelper(this).Handle;
        if (!RecycleBin.TrySend(node.FullPath, owner, out var error))
        {
            MessageBox.Show(this, $"Could not delete {node.FullPath}.\n\n{error}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        long freed = node.Allocated;
        node.RemoveFromTree(Treemap.SizeMode);
        _root?.RegisterFreedSpace(freed, Treemap.SizeMode);
        Treemap.SelectedNode = null;
        Treemap.Refresh();
        ShowNodeInfo(null);
        _breadcrumbFor = null; // sizes changed
        UpdateNavigation();
        StatusScan.Text = $"Moved {size} to the Recycle Bin";
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

    private async void ScanDrive_Click(object sender, RoutedEventArgs e)
    {
        if (DriveCombo.SelectedItem is DriveEntry drive)
            await StartScanAsync(drive.Path);
    }

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
        if (_applyingSettings || Treemap is null || LegendPanel is null || PaletteCombo is null) return;

        _settings.Palette = (PaletteCombo.SelectedItem as ColorScheme ?? Palette.Default).Name;
        _settings.ColorMode = (ColorCombo.SelectedIndex == 1 ? ColorMode.ByFileType : ColorMode.ByDepth).ToString();
        ApplySettings();
    }

    private void TreemapMenu_Opened(object sender, RoutedEventArgs e)
    {
        var node = Treemap.SelectedNode;
        bool hasNode = node is not null && node.IsReal && !IsScanning;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;

        MenuFocus.IsEnabled = node is not null && !node.IsFreeSpace && !IsScanning && folder is not null;
        MenuUp.IsEnabled = !IsScanning && Treemap.FocusedFolder?.Parent is not null;
        MenuFit.IsEnabled = !IsScanning && Treemap.Root is not null && Treemap.Zoom > 1.0001;
        MenuOpen.IsEnabled = hasNode;
        MenuExplorer.IsEnabled = hasNode;
        MenuCopy.IsEnabled = hasNode;
        MenuDelete.IsEnabled = hasNode && node!.Parent is not null && !ReferenceEquals(node, Treemap.Root);
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

    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { } node) CopyPath(node);
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { } node) DeleteNode(node);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
            case Key.Delete when Treemap.SelectedNode is { } deleteNode:
                DeleteNode(deleteNode);
                break;
            case Key.C when ctrl && Treemap.SelectedNode is { } copyNode:
                CopyPath(copyNode);
                break;
            case Key.C when !ctrl:
                ToggleSetting(v => _settings.Cushion = v, _settings.Cushion, "Cushion shading");
                break;
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
