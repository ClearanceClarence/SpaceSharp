using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// All settings in one place. Every change is applied to the main window immediately and saved,
/// so there is no OK/Cancel: the Close button just closes.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] LabelSizes = { "Smallest", "Smaller", "Normal", "Large", "Larger" };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly MainWindow _main;
    private StackPanel? _card;

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _main = main;
        Build();
    }

    private string _tab = "General";
    private static readonly string[] Tabs = { "General", "Appearance", "Treemap", "Map", "Scanning" };

    private void Build()
    {
        BuildTabs();
        Body.Children.Clear();
        switch (_tab)
        {
            case "General":
                Section(Strings.Get("Settings_Language"));
                Row(Strings.Get("Settings_Language_AppLanguage"), Strings.Get("Settings_Language_Description"), LanguageCombo());

                Section(Strings.Get("Settings_Updates"));
                Row(Strings.Get("Settings_CheckUpdates"), Updater.Instance.IsInstalled
                        ? Strings.Get("Settings_CheckUpdates_Desc")
                        : Strings.Get("Settings_CheckUpdates_Portable"),
                    Switch(_settings.CheckForUpdates, v => _settings.CheckForUpdates = v));

                Section(Strings.Get("Settings_Safety"));
                Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
                    Switch(_settings.ConfirmDelete, v => _settings.ConfirmDelete = v));

                Section(Strings.Get("Settings_Windows"));
                Row(Strings.Get("Settings_ExplorerMenu"), Strings.Get("Settings_ExplorerMenu_Desc"),
                    Switch(_settings.ExplorerMenu, v =>
            {
                        _settings.ExplorerMenu = v;
                        try
                {
                            if (v) ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith")); else ExplorerIntegration.Uninstall();
                        }
                        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                            Dialog.Error(this, Strings.Get("Settings_ExplorerMenu"), ex.Message);
                        }
                    }));
                Row(Strings.Get("Settings_CommandLine"), Strings.Get("Settings_CommandLine_Desc"), null);
                _card!.Children.Add(Themed(new TextBox
        {
                    Text = CommandLine.HelpText.TrimEnd(), IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                    FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(16, 0, 16, 12), TextWrapping = TextWrapping.NoWrap
                }, TextBox.ForegroundProperty, "TextDim"));
                break;
            case "Appearance":
                Section(Strings.Get("Settings_Appearance"));
                Row(Strings.Get("Settings_Theme"), Strings.Get("Settings_Theme_Desc"),
                    Combo(new[] { Strings.Get("Theme_MatchWindows"), Strings.Get("Theme_LightName"), Strings.Get("Theme_DarkName") }, (int)ThemeManager.Choice,
                        i => _settings.Theme = ((AppTheme)i).ToString()));
                Row(Strings.Get("Settings_Palette"), Strings.Get("Settings_Palette_Desc"),
                    Combo(Palette.Schemes.Select(s => s.IsCustom ? Strings.Format("Settings_PaletteCustomSuffix", s.Name) : s.Name).ToArray(), Palette.Schemes.ToList().IndexOf(Palette.Find(_settings.Palette)),
                        i => _settings.Palette = Palette.Schemes[i].Name));
                Row(Strings.Get("Settings_ColorBy"), Strings.Get("Settings_ColorBy_Desc"),
                    Combo(new[] { Strings.Get("ColorModeName_TopFolder"), Strings.Get("ColorModeName_Depth"), Strings.Get("ColorModeName_FileType"), Strings.Get("ColorModeName_Change") },
                        Enum.TryParse<ColorMode>(_settings.ColorMode, out var colorMode) ? (int)colorMode : 0,
                        i => _settings.ColorMode = ((ColorMode)i).ToString()));
                Row(Strings.Get("Settings_MapStyle"), Strings.Get("Settings_MapStyle_Desc"),
                    Combo(Enum.GetNames<MapStyle>().Select(n => Strings.Get("MapStyle_" + n)).ToArray(), Math.Max(0, Array.IndexOf(Enum.GetNames<MapStyle>(), _settings.MapStyle ?? "Classic")),
                        i => _settings.MapStyle = Enum.GetNames<MapStyle>()[i]));
                Row(Strings.Get("Settings_LabelSize"), Strings.Get("Settings_LabelSize_Desc"),
                    Combo(LabelSizes.Select(n => Strings.Get("LabelSize_" + n)).ToArray(), Array.IndexOf(LabelSizes, _settings.LabelSize ?? "Normal"),
                        i => _settings.LabelSize = LabelSizes[i]));
                Row(Strings.Get("Settings_OutlinedLabels"), Strings.Get("Settings_OutlinedLabels_Desc"),
                    Switch(_settings.LabelHalo, v => _settings.LabelHalo = v));

                Row(Strings.Get("Settings_CustomPalettes"), PaletteNote(),
                    PaletteButtons());
                break;
            case "Treemap":
                Section(Strings.Get("Settings_Treemap"), Strings.Get("Settings_Treemap_Desc"));
                Row(Strings.Get("Treemap_Density"), Strings.Get("Treemap_Density_Desc"),
                    Combo(Enum.GetNames<MapDensity>().Select(n => Strings.Get("Density_" + n)).ToArray(), Math.Max(0, Array.IndexOf(Enum.GetNames<MapDensity>(), _settings.Density)),
                        i => _settings.Density = Enum.GetNames<MapDensity>()[i]));
                Row(Strings.Get("Treemap_Bias"), Strings.Get("Treemap_Bias_Desc"), BiasSlider());
                Row(Strings.Get("Treemap_Padding"), Strings.Get("Treemap_Padding_Desc"),
                    Combo(Enumerable.Range(0, 7).Select(n => Strings.Format("Unit_Pixels", n)).ToArray(), Math.Clamp(_settings.Padding, 0, 6), i => _settings.Padding = i));
                Row(Strings.Get("Treemap_Border"), Strings.Get("Treemap_Border_Desc"),
                    Combo(Enumerable.Range(0, 4).Select(n => n == 0 ? Strings.Get("Treemap_NoBorder") : Strings.Format("Unit_Pixels", n)).ToArray(), Math.Clamp(_settings.BorderThickness, 0, 3), i => _settings.BorderThickness = i));
                Row(Strings.Get("Treemap_Font"), Strings.Get("Treemap_Font_Desc"),
                    Combo(MapFonts, Math.Max(0, Array.IndexOf(MapFonts, _settings.MapFont)), i => _settings.MapFont = MapFonts[i]));
                Row(Strings.Get("Treemap_Files"), Strings.Get("Treemap_Files_Desc"),
                    Checks((Strings.Get("Treemap_CenterNames"), _settings.FileCenterNames, v => _settings.FileCenterNames = v),
                           (Strings.Get("Treemap_ShowSizes"), _settings.FileShowSizes, v => _settings.FileShowSizes = v)));
                Row(Strings.Get("Treemap_Folders"), Strings.Get("Treemap_Folders_Desc"),
                    Checks((Strings.Get("Treemap_CenterNames"), _settings.FolderCenterNames, v => _settings.FolderCenterNames = v),
                           (Strings.Get("Treemap_ShowSizes"), _settings.FolderShowSizes, v => _settings.FolderShowSizes = v),
                           (Strings.Get("Treemap_ShowCounts"), _settings.FolderShowCounts, v => _settings.FolderShowCounts = v)));
                break;
            case "Map":
                Section(Strings.Get("Settings_Map"));
                Row(Strings.Get("Settings_SizeMeasure"), Strings.Get("Settings_SizeMeasure_Desc"),
                    Combo(new[] { Strings.Get("SizeMeasure_FileSize"), Strings.Get("SizeMeasure_OnDisk") }, _settings.SizeOnDisk ? 1 : 0, i => _settings.SizeOnDisk = i == 1));
                Row(Strings.Get("Settings_ShowFreeSpace"), Strings.Get("Settings_ShowFreeSpace_Desc"),
                    Switch(_settings.ShowFreeSpace, v => _settings.ShowFreeSpace = v));
                Row(Strings.Get("Settings_MergeChains"), Strings.Get("Settings_MergeChains_Desc"),
                    Switch(_settings.MergeChains, v => _settings.MergeChains = v));
                Row(Strings.Get("Settings_HoverDetails"), Strings.Get("Settings_HoverDetails_Desc"),
                    Switch(_settings.ShowTooltips, v => _settings.ShowTooltips = v));
                Row(Strings.Get("Settings_SidePanel"), Strings.Get("Settings_SidePanel_Desc"),
                    Switch(_settings.ShowSidePanel, v => _settings.ShowSidePanel = v));
                Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
                    Switch(_settings.AnimateZoom, v => _settings.AnimateZoom = v));
                break;
            case "Scanning":
                Section(Strings.Get("Settings_Scanning"), Strings.Get("Settings_Scanning_Desc"));
                Row(Strings.Get("Settings_FastScan"), Strings.Get("Settings_FastScan_Desc"),
                    Switch(_settings.FastNtfsScan, v => _settings.FastNtfsScan = v));
                Row(Strings.Get("Settings_LeaveOut"), Strings.Get("Settings_LeaveOut_Desc"),
                    ExclusionsEditor());
                Row(Strings.Get("Settings_Reopen"), Strings.Get("Settings_Reopen_Desc"),
                    Switch(_settings.ReopenLastScan, v => _settings.ReopenLastScan = v));
                Row(Strings.Get("Settings_KeepScans"), Strings.Get("Settings_KeepScans_Desc"), KeepScansSlider());
        Row(Strings.Get("Settings_IncludeHidden"), Strings.Get("Settings_IncludeHidden_Desc"),
                    Switch(_settings.IncludeHidden, v => _settings.IncludeHidden = v));
                Row(Strings.Get("Settings_HardLinks"), Strings.Get("Settings_HardLinks_Desc"),
                    Switch(_settings.DetectHardLinks, v => _settings.DetectHardLinks = v));
                break;
        }
        if (_tab == "General")
        {
            var note = Themed(new TextBlock
            {
                Text = Strings.Get("Settings_SavedIn"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
            }, TextBlock.ForegroundProperty, "TextDim");
            Body.Children.Add(note);
        }
    }

    /// <summary>The row of tabs above the page; the chosen one is drawn with the accent underline like the side panel's tabs.</summary>
    private void BuildTabs()
    {
        TabBar.Children.Clear();
        foreach (var tab in Tabs)
        {
            var button = new Button { Content = Strings.Get("SettingsTab_" + tab), Style = (Style)FindResource("TabButton"), Margin = new Thickness(0, 0, 4, 0), Height = 32 };
            bool active = tab == _tab;
            button.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) button.SetResourceReference(BackgroundProperty, "Control");
            button.Click += (_, _) => { _tab = tab; Body.Dispatcher.BeginInvoke(Build); Scroller.ScrollToTop(); };
            TabBar.Children.Add(button);
        }
    }

    // ------------------------------------------------------------ builders

    private void Section(string title, string? note = null)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        if (note is not null)
        {
            Body.Children.Add(Themed(new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) },
                TextBlock.ForegroundProperty, "TextDim"));
        }

        _card = new StackPanel();
        Body.Children.Add(new Border { Style = (Style)FindResource("SettingsCard"), Child = _card });
    }

    private void Row(string title, string description, FrameworkElement? control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Themed(new TextBlock
        {
            Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
        }, TextBlock.ForegroundProperty, "TextDim"));

        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        var card = _card!;
        if (card.Children.Count > 0)
        {
            card.Children.Add(Themed(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0) },
                Border.BackgroundProperty, "Stroke"));
        }
        card.Children.Add(grid);
    }

    private CheckBox Switch(bool value, Action<bool> onChanged)
    {
        var box = new CheckBox { IsChecked = value, Style = (Style)FindResource("Switch") };
        box.Checked += (_, _) => Change(() => onChanged(true));
        box.Unchecked += (_, _) => Change(() => onChanged(false));
        return box;
    }

    private ComboBox LanguageCombo()
    {
        var cultures = AppLanguages.Available();
        var labels = new List<string> { Strings.Format("Settings_Language_System", AppLanguages.DisplayName(AppLanguages.SystemCulture)) };
        labels.AddRange(cultures.Select(AppLanguages.DisplayName));
        int selected = 0;
        if (!string.IsNullOrWhiteSpace(_settings.Language))
        {
            int i = cultures.ToList().FindIndex(c => string.Equals(c.Name, _settings.Language, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) selected = i + 1;
        }
        return Combo(labels.ToArray(), selected, i =>
        {
            string? chosen = i == 0 ? null : cultures[i - 1].Name;
            if (chosen == _settings.Language) return;
            _settings.Language = chosen;
            _settings.Save();
            string name = i == 0 ? AppLanguages.DisplayName(AppLanguages.SystemCulture) : AppLanguages.DisplayName(cultures[i - 1]);
            if (Dialog.Confirm(this, Strings.Get("Settings_Language_RestartTitle"), Strings.Format("Settings_Language_RestartMessage", name), Strings.Get("Settings_Language_RestartNow"), Strings.Get("Settings_Language_Later"))) _main.RestartApp();
        });
    }

    private static string PaletteNote()
    {
        int custom = Palette.Schemes.Count(s => !IsBuiltIn(s));
        string note = Strings.Get("Palettes_Note");
        if (custom > 0) note += custom == 1 ? Strings.Get("Palettes_LoadedOne") : Strings.Format("Palettes_LoadedMany", custom);
        if (CustomPalettes.LastErrors.Count > 0) note += Strings.Get("Palettes_NotLoaded") + string.Join("; ", CustomPalettes.LastErrors);
        return note;
    }

    private static bool IsBuiltIn(ColorScheme s) => !s.IsCustom;

    private StackPanel PaletteButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var open = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Palettes_OpenFolder"), Tag = "\uE838", Height = 30 };
        open.Click += (_, _) =>
        {
            try
            {
                CustomPalettes.WriteExample();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{CustomPalettes.Folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
            }
        };
        var reload = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Palettes_Reload"), Tag = "\uE72C", Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        reload.Click += (_, _) =>
        {
            Palette.Reload();
            _main.RefreshPalettes();
            Build(); // redraw the page so the palette list and the note update
        };
        panel.Children.Add(open);
        panel.Children.Add(reload);
        return panel;
    }

    private static readonly string[] MapFonts = { "Segoe UI", "Segoe UI Variable Text", "Calibri", "Arial", "Verdana", "Tahoma", "Consolas", "Cascadia Mono" };

    private static readonly string[] ExclusionSuggestions = { "node_modules", "$Recycle.Bin", "*.tmp", ".git", "System Volume Information" };

    /// <summary>
    /// The "Leave out" list as chips: each pattern is a chip with a remove button, a box adds a new one (Enter or the
    /// Add button), and the common ones sit below as one-click suggestions until they are in the list.
    /// </summary>
    private FrameworkElement ExclusionsEditor()
    {
        var panel = new StackPanel { Width = 260 };
        var chips = new WrapPanel();
        var input = new TextBox { Style = (Style)FindResource("InputBox"), Tag = Strings.Get("Exclude_Placeholder"), Width = 196 };
        var add = new Button { Style = (Style)FindResource("ToolButton"), Tag = "\uE710", Content = Strings.Get("Exclude_Add"), Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        var suggestions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        List<string> Current() => NamePatterns.Parse(_settings.ExcludePatterns).Patterns.ToList();
        void Save(IEnumerable<string> list) { _settings.ExcludePatterns = string.Join("\n", list); _settings.Save(); Render(); }
        void Add(string? text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var list = Current();
            if (!list.Contains(text, StringComparer.OrdinalIgnoreCase)) list.Add(text);
            input.Text = string.Empty;
            Save(list);
        }
        void Render()
        {
            chips.Children.Clear();
            var list = Current();
            foreach (var pattern in list)
            {
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 6, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, "Control");
                chip.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = pattern, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Style = (Style)FindResource("LinkButton"), Content = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, Margin = new Thickness(8, 0, 0, 0), ToolTip = Strings.Get("Exclude_Remove") };
                remove.SetResourceReference(ForegroundProperty, "TextDim");
                string captured = pattern;
                remove.Click += (_, _) => Save(Current().Where(p => !string.Equals(p, captured, StringComparison.OrdinalIgnoreCase)));
                row.Children.Add(remove);
                chip.Child = row;
                chips.Children.Add(chip);
            }
            suggestions.Children.Clear();
            var missing = ExclusionSuggestions.Where(s => !list.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                suggestions.Children.Add(Themed(new TextBlock { Text = Strings.Get("Exclude_Suggestions"), FontSize = 12, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim"));
                foreach (var s in missing)
                {
                    var link = new Button { Style = (Style)FindResource("LinkButton"), Content = s, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 0, 10, 4) };
                    link.Click += (_, _) => Add(s);
                    suggestions.Children.Add(link);
                }
            }
        }
        add.Click += (_, _) => Add(input.Text);
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Add(input.Text); e.Handled = true; } };
        Render();

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
        inputRow.Children.Add(input); inputRow.Children.Add(add);
        panel.Children.Add(chips); panel.Children.Add(inputRow); panel.Children.Add(suggestions);
        return panel;
    }

    private static readonly int[] KeepSteps = { 7, 30, 90, 180, 365, 0 };   // 0 = forever, last so the slider reads short to long

    /// <summary>A slider over fixed steps (a week to forever) with the chosen value named above it, and a button to clear the folder now.</summary>
    private FrameworkElement KeepScansSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var label = Themed(new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) }, TextBlock.ForegroundProperty, "Text");
        int index = Array.IndexOf(KeepSteps, _settings.KeepScansDays); if (index < 0) index = 2;
        var slider = new Slider { Minimum = 0, Maximum = KeepSteps.Length - 1, Value = index, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1 };
        void Show(int i) => label.Text = KeepSteps[i] == 0 ? Strings.Get("KeepScans_Forever") : KeepSteps[i] % 365 == 0 ? Strings.Get("KeepScans_OneYear") : KeepSteps[i] >= 30 ? Strings.Format("KeepScans_Months", KeepSteps[i] / 30) : Strings.Format("KeepScans_Days", KeepSteps[i]);
        Show(index);
        slider.ValueChanged += (_, e) => { int i = (int)Math.Round(e.NewValue); _settings.KeepScansDays = KeepSteps[i]; Show(i); _settings.Save(); };
        var clear = new Button { Style = (Style)FindResource("DangerOutlineButton"), Content = Strings.Get("KeepScans_DeleteAll"), Tag = "\uE74D", Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        clear.Click += (_, _) =>
        {
            int count = ScanFile.RecentAutoSaves(int.MaxValue).Count;
            if (count == 0) { Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Get("KeepScans_NoneToDelete")); return; }
            if (Dialog.Confirm(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_DeleteQuestion", count), Strings.Get("Delete_ErrorTitle"), danger: true))
            {
                int removed = ScanFile.DeleteAll();
                Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_Deleted", removed));
            }
        };
        panel.Children.Add(label); panel.Children.Add(slider); panel.Children.Add(clear);
        return panel;
    }

    /// <summary>Horizontal … Equal … Vertical, snapping to the middle so "equal" is easy to hit.</summary>
    private FrameworkElement BiasSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var slider = new Slider { Minimum = -1, Maximum = 1, Value = _settings.Bias, TickFrequency = 0.25, IsSnapToTickEnabled = true, SmallChange = 0.25, LargeChange = 0.5 };
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Themed(new TextBlock { Text = Strings.Get("Treemap_Horizontal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left }, TextBlock.ForegroundProperty, "TextDim");
        var m = Themed(new TextBlock { Text = Strings.Get("Treemap_Equal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim");
        var r = Themed(new TextBlock { Text = Strings.Get("Treemap_Vertical"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }, TextBlock.ForegroundProperty, "TextDim");
        Grid.SetColumn(m, 1); Grid.SetColumn(r, 2);
        labels.Children.Add(l); labels.Children.Add(m); labels.Children.Add(r);
        slider.ValueChanged += (_, e) => { _settings.Bias = Math.Round(e.NewValue, 2); _main.ApplySettings(); };
        panel.Children.Add(slider); panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Several on/off choices in one row: a label and the same toggle switch the other rows use, one per line.</summary>
    private FrameworkElement Checks(params (string Label, bool Value, Action<bool> OnChanged)[] items)
    {
        var grid = new Grid { Width = 220 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        foreach (var (label, value, onChanged) in items)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = Themed(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) }, TextBlock.ForegroundProperty, "Text");
            var toggle = Switch(value, onChanged);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(text, row); Grid.SetRow(toggle, row); Grid.SetColumn(toggle, 1);
            grid.Children.Add(text); grid.Children.Add(toggle);
            row++;
        }
        return grid;
    }

    private ComboBox Combo(string[] items, int selectedIndex, Action<int> onChanged)
    {
        var combo = new ComboBox { Width = 170, ItemsSource = items, SelectedIndex = Math.Max(0, selectedIndex) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) Change(() => onChanged(combo.SelectedIndex));
        };
        return combo;
    }

    private void Change(Action update)
    {
        update();
        _main.ApplySettings();
    }

    private static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    // ------------------------------------------------------------ buttons

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm(this, Strings.Get("Settings_ResetTitle"), Strings.Get("Settings_ResetQuestion"), Strings.Get("Settings_ResetButton"))) return;

        _settings.ResetToDefaults();
        _main.ApplySettings();
        Build();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
