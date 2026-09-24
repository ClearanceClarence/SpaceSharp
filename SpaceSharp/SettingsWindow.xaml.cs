using System.Windows;
using System.Windows.Controls;
using SpaceSharp.Controls;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// All settings in one place. Every change is applied to the main window immediately and saved,
/// so there is no OK/Cancel: the Close button just closes.
/// </summary>
public partial class SettingsWindow : Window
{
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

        Section("Appearance");
        Row("Theme", "Follow the Windows setting, or always use light or dark.",
            Combo(new[] { "Match Windows", "Light", "Dark" }, (int)ThemeManager.Choice,
                i => _settings.Theme = ((AppTheme)i).ToString()));
        Row("Palette", "The colors used for boxes in the map.",
            Combo(Palette.Schemes.Select(s => s.Name).ToArray(), Palette.Schemes.ToList().IndexOf(Palette.Find(_settings.Palette)),
                i => _settings.Palette = Palette.Schemes[i].Name));
        Row("Color by", "Color each nesting level differently, or color files by their type.",
            Combo(new[] { "Folder depth", "File type" }, _settings.ColorMode == nameof(ColorMode.ByFileType) ? 1 : 0,
                i => _settings.ColorMode = (i == 1 ? ColorMode.ByFileType : ColorMode.ByDepth).ToString()));
        Row("Cushion shading", "Soft light-to-dark shading on every box. Makes sizes easier to compare than flat colors. Shortcut: C.",
            Switch(_settings.Cushion, v => _settings.Cushion = v));

        Section("Map");
        Row("Size measure", "Size on disk uses the compressed size of compressed, sparse and cloud files and rounds every file up to whole clusters, like Explorer's \"Size on disk\".",
            Combo(new[] { "File size", "Size on disk" }, _settings.SizeOnDisk ? 1 : 0, i => _settings.SizeOnDisk = i == 1));
        Row("Show free space", "Add a gray block for the unused space when a whole drive is scanned. It grows when you delete files.",
            Switch(_settings.ShowFreeSpace, v => _settings.ShowFreeSpace = v));
        Row("Merge single-folder chains", "Draw folders that only contain one folder (Users › you › AppData › Local) as one box with a combined title.",
            Switch(_settings.MergeChains, v => _settings.MergeChains = v));
        Row("Group small items", "Folders with hundreds of small files show one \"312 files\" box instead of a grid of specks. Zooming in shows them individually. Shortcut: G.",
            Switch(_settings.GroupSmallItems, v => _settings.GroupSmallItems = v));
        Row("Animate zoom", "Fly into folders instead of jumping to them.",
            Switch(_settings.AnimateZoom, v => _settings.AnimateZoom = v));

        Section("Scanning", "Changes here take effect on the next scan (F5).");
        Row("Include hidden and system files", "Off leaves out files and folders with the Hidden or System attribute, such as pagefile.sys.",
            Switch(_settings.IncludeHidden, v => _settings.IncludeHidden = v));
        Row("Count hard links once", "Reads every file's link count so data with several names, such as Windows' WinSxS folder, is counted once. Slower on large drives.",
            Switch(_settings.DetectHardLinks, v => _settings.DetectHardLinks = v));

        Section("Safety");
        Row("Confirm before moving to the Recycle Bin", "Ask before deleting. Deleted items can always be restored from the Recycle Bin.",
            Switch(_settings.ConfirmDelete, v => _settings.ConfirmDelete = v));

        var note = Themed(new TextBlock
        {
            Text = "Settings are saved in %AppData%\\SpaceSharp\\settings.json.",
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
        var answer = MessageBox.Show(this, "Reset every setting to its default value?", "Reset settings",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _settings.ResetToDefaults();
        _main.ApplySettings();
        Build();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
