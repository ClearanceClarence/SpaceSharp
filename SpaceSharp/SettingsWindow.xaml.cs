using System.Windows;
using System.Windows.Controls;
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

    private void Build()
    {
        Body.Children.Clear();

        Section(Strings.Get("Settings_Language"));
        Row(Strings.Get("Settings_Language_AppLanguage"), Strings.Get("Settings_Language_Description"), LanguageCombo());

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

        Section(Strings.Get("Settings_Map"));
        Row(Strings.Get("Settings_SizeMeasure"), Strings.Get("Settings_SizeMeasure_Desc"),
            Combo(new[] { Strings.Get("SizeMeasure_FileSize"), Strings.Get("SizeMeasure_OnDisk") }, _settings.SizeOnDisk ? 1 : 0, i => _settings.SizeOnDisk = i == 1));
        Row(Strings.Get("Settings_ShowFreeSpace"), Strings.Get("Settings_ShowFreeSpace_Desc"),
            Switch(_settings.ShowFreeSpace, v => _settings.ShowFreeSpace = v));
        Row(Strings.Get("Settings_MergeChains"), Strings.Get("Settings_MergeChains_Desc"),
            Switch(_settings.MergeChains, v => _settings.MergeChains = v));
        Row(Strings.Get("Settings_GroupSmall"), Strings.Get("Settings_GroupSmall_Desc"),
            Switch(_settings.GroupSmallItems, v => _settings.GroupSmallItems = v));
        Row(Strings.Get("Settings_HoverDetails"), Strings.Get("Settings_HoverDetails_Desc"),
            Switch(_settings.ShowTooltips, v => _settings.ShowTooltips = v));
        Row(Strings.Get("Settings_SidePanel"), Strings.Get("Settings_SidePanel_Desc"),
            Switch(_settings.ShowSidePanel, v => _settings.ShowSidePanel = v));
        Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
            Switch(_settings.AnimateZoom, v => _settings.AnimateZoom = v));

        Section(Strings.Get("Settings_Scanning"), Strings.Get("Settings_Scanning_Desc"));
        Row(Strings.Get("Settings_FastScan"), Strings.Get("Settings_FastScan_Desc"),
            Switch(_settings.FastNtfsScan, v => _settings.FastNtfsScan = v));
        Row(Strings.Get("Settings_LeaveOut"), Strings.Get("Settings_LeaveOut_Desc"),
            MultiLine(_settings.ExcludePatterns, v => _settings.ExcludePatterns = v));
        Row(Strings.Get("Settings_Reopen"), Strings.Get("Settings_Reopen_Desc"),
            Switch(_settings.ReopenLastScan, v => _settings.ReopenLastScan = v));
        Row(Strings.Get("Settings_IncludeHidden"), Strings.Get("Settings_IncludeHidden_Desc"),
            Switch(_settings.IncludeHidden, v => _settings.IncludeHidden = v));
        Row(Strings.Get("Settings_HardLinks"), Strings.Get("Settings_HardLinks_Desc"),
            Switch(_settings.DetectHardLinks, v => _settings.DetectHardLinks = v));

        Section(Strings.Get("Settings_Updates"));
        Row(Strings.Get("Settings_CheckUpdates"), Updater.Instance.IsInstalled
                ? Strings.Get("Settings_CheckUpdates_Desc")
                : Strings.Get("Settings_CheckUpdates_Portable"),
            Switch(_settings.CheckForUpdates, v => _settings.CheckForUpdates = v));

        Section(Strings.Get("Settings_Safety"));
        Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
            Switch(_settings.ConfirmDelete, v => _settings.ConfirmDelete = v));

        var note = Themed(new TextBlock
        {
            Text = Strings.Get("Settings_SavedIn"),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
        }, TextBlock.ForegroundProperty, "TextDim");
        Body.Children.Add(note);
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

    private void Row(string title, string description, FrameworkElement control)
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

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);

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

    private TextBox MultiLine(string value, Action<string> onChanged)
    {
        var box = new TextBox
        {
            Width = 220, MinHeight = 64, MaxHeight = 140, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = value, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12
        };
        box.LostFocus += (_, _) => { if (box.Text != value) { value = box.Text; onChanged(box.Text); _main.ApplySettings(); } };
        return box;
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
