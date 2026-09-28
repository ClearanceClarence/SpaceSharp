using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// The filter options panel: a visual way to build the same filter the text box accepts.
/// Every control writes back into the text box, so the two never disagree.
/// </summary>
public partial class MainWindow
{
    private static readonly (string Label, long Bytes)[] SizeSteps =
    {
        (Strings.Get("Filter_AnySize"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20),
        ("500 MB", 500L << 20), ("1 GB", 1L << 30), ("5 GB", 5L << 30), ("10 GB", 10L << 30), ("50 GB", 50L << 30)
    };

    private static readonly (string Label, int Days)[] AgeSteps =
    {
        (Strings.Get("Filter_AnyTime"), 0), (Strings.Get("Filter_1Month"), 30), (Strings.Get("Filter_3Months"), 90), (Strings.Get("Filter_6Months"), 180),
        (Strings.Get("Filter_1Year"), 365), (Strings.Get("Filter_2Years"), 730), (Strings.Get("Filter_5Years"), 1825)
    };

    private static readonly (string Label, string Filter)[] Presets =
    {
        (Strings.Get("Preset_LargeFiles"), ">1GB is:file"),
        (Strings.Get("Preset_BigVideos"), "type:video >500MB"),
        (Strings.Get("Preset_InstallersArchives"), "type:archives,programs >100MB"),
        (Strings.Get("Preset_Untouched2Years"), "older than 2 years is:file"),
        (Strings.Get("Preset_BigAndOld"), ">500MB older than 1 year is:file"),
        (Strings.Get("Preset_RecentlyChanged"), "newer than 1 week is:file")
    };

    private bool _buildingPanel;
    private TextBox? _nameBox;
    private readonly Dictionary<FileCategory, ToggleButton> _typeChips = new();
    private ComboBox? _minCombo, _maxCombo, _ageCombo, _kindCombo;

    private void FilterPanel_Click(object sender, RoutedEventArgs e)
    {
        if (FilterPopup.IsOpen)
        {
            FilterPopup.IsOpen = false;
            return;
        }

        if (FilterPanelBody.Children.Count == 0) BuildFilterPanel();
        LoadPanelFromText();
        FilterPopup.IsOpen = true;
        _nameBox?.Focus();
    }

    // ---------------------------------------------------------------- build

    private void BuildFilterPanel()
    {
        var body = FilterPanelBody;

        body.Children.Add(PanelHeading(Strings.Get("Filter_QuickFilters")));
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        foreach (var (label, filter) in Presets)
        {
            var chip = new ToggleButton { Content = label, Style = (Style)FindResource("Chip"), Tag = filter };
            chip.Click += (sender, _) =>
            {
                ((ToggleButton)sender).IsChecked = false; // presets act like buttons
                FilterBox.Text = filter;
                LoadPanelFromText();
            };
            presets.Children.Add(chip);
        }
        body.Children.Add(presets);
        body.Children.Add(Divider());

        body.Children.Add(PanelHeading(Strings.Get("Filter_Name")));
        _nameBox = new TextBox
        {
            Style = (Style)FindResource("FilterBox"), Height = 32, Margin = new Thickness(0, 6, 0, 10),
            Tag = Strings.Get("Filter_NamePlaceholder")
        };
        _nameBox.TextChanged += (_, _) => WritePanelToText();
        body.Children.Add(_nameBox);

        body.Children.Add(PanelHeading(Strings.Get("Filter_Type")));
        var types = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var chip = new ToggleButton { Content = Strings.Category(category), Style = (Style)FindResource("Chip") };
            chip.Checked += (_, _) => WritePanelToText();
            chip.Unchecked += (_, _) => WritePanelToText();
            _typeChips[category] = chip;
            types.Children.Add(chip);
        }
        body.Children.Add(types);
        body.Children.Add(Divider());

        var sizes = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        sizes.ColumnDefinitions.Add(new ColumnDefinition());
        sizes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        sizes.ColumnDefinitions.Add(new ColumnDefinition());
        _minCombo = LabeledCombo(sizes, 0, Strings.Get("Filter_LargerThan"), SizeSteps.Select(s => s.Label));
        _maxCombo = LabeledCombo(sizes, 2, Strings.Get("Filter_SmallerThan"), SizeSteps.Select(s => s.Label));
        body.Children.Add(sizes);

        var when = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        when.ColumnDefinitions.Add(new ColumnDefinition());
        when.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        when.ColumnDefinitions.Add(new ColumnDefinition());
        _ageCombo = LabeledCombo(when, 0, Strings.Get("Filter_NotModifiedFor"), AgeSteps.Select(a => a.Label));
        _kindCombo = LabeledCombo(when, 2, Strings.Get("Filter_Show"), new[] { Strings.Get("Filter_FilesAndFolders"), Strings.Get("Filter_FilesOnly"), Strings.Get("Filter_FoldersOnly") });
        body.Children.Add(when);
    }

    private Border Divider() =>
        Themed(new Border { Height = 1, Margin = new Thickness(0, 8, 0, 10) }, Border.BackgroundProperty, "Stroke");

    /// <summary>The live result line and Clear button in the popup footer; called whenever the filter is applied.</summary>
    private void UpdateFilterFooter()
    {
        if (FilterFooterInfo is null) return;
        bool active = _filter is not null && !_filter.IsEmpty;
        FilterClearButton.IsEnabled = active;
        FilterFooterInfo.Text = !active ? Strings.Get("Filter_NoFilter")
            : _root is null ? _filter!.Description
            : _filterResult is null || _filterResult.FileCount == 0 ? Strings.Get("Filter_NothingMatchesShort")
            : Strings.Format("Filter_MatchCount", _filterResult.FileCount, SizeFormatter.Format(_filterResult.Bytes));
    }

    private TextBlock PanelHeading(string text) =>
        Themed(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) },
            TextBlock.ForegroundProperty, "TextDim");

    private ComboBox LabeledCombo(Grid grid, int column, string label, IEnumerable<string> items)
    {
        var stack = new StackPanel();
        stack.Children.Add(PanelHeading(label));
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 0), Height = 32 };
        combo.SelectionChanged += (_, _) => WritePanelToText();
        stack.Children.Add(combo);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
        return combo;
    }

    // ---------------------------------------------------------------- sync

    /// <summary>Text box → panel controls.</summary>
    private void LoadPanelFromText()
    {
        if (_nameBox is null) return;
        _buildingPanel = true;

        var spec = FilterSpec.Parse(FilterBox.Text);
        _nameBox.Text = string.Join(" ", spec.Patterns.Concat(spec.Words));
        foreach (var (category, chip) in _typeChips) chip.IsChecked = spec.Types.Contains(category);
        _minCombo!.SelectedIndex = Math.Max(0, Array.FindIndex(SizeSteps, s => s.Bytes == (spec.MinBytes ?? 0)));
        _maxCombo!.SelectedIndex = Math.Max(0, Array.FindIndex(SizeSteps, s => s.Bytes == (spec.MaxBytes ?? 0)));
        _ageCombo!.SelectedIndex = Math.Max(0, Array.FindIndex(AgeSteps, a => a.Days == (spec.OlderThanDays ?? 0)));
        _kindCombo!.SelectedIndex = spec.Kind switch { ItemKind.Files => 1, ItemKind.Folders => 2, _ => 0 };

        _buildingPanel = false;
    }

    /// <summary>Panel controls → text box (which then applies the filter).</summary>
    private void WritePanelToText()
    {
        if (_buildingPanel || _nameBox is null) return;

        var spec = new FilterSpec();
        foreach (var token in _nameBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.IndexOfAny(new[] { '*', '?' }) >= 0) spec.Patterns.Add(token);
            else if (token.StartsWith('.') && token.Length > 1) spec.Patterns.Add("*" + token);
            else spec.Words.Add(token);
        }
        foreach (var (category, chip) in _typeChips)
            if (chip.IsChecked == true) spec.Types.Add(category);

        long min = SizeSteps[Math.Max(0, _minCombo!.SelectedIndex)].Bytes;
        long max = SizeSteps[Math.Max(0, _maxCombo!.SelectedIndex)].Bytes;
        spec.MinBytes = min > 0 ? min : null;
        spec.MaxBytes = max > 0 ? max : null;
        int days = AgeSteps[Math.Max(0, _ageCombo!.SelectedIndex)].Days;
        spec.OlderThanDays = days > 0 ? days : null;
        spec.Kind = _kindCombo!.SelectedIndex switch { 1 => ItemKind.Files, 2 => ItemKind.Folders, _ => ItemKind.Any };

        // Keep a "newer than" typed by hand; the panel has no control for it.
        var current = FilterSpec.Parse(FilterBox.Text);
        spec.NewerThanDays = current.NewerThanDays;

        string text = spec.ToText();
        if (text != FilterBox.Text) FilterBox.Text = text;
    }
}
