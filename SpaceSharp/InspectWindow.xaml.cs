using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// Details about one item or a selection: sizes, shares, dates, what is inside a folder and which
/// file types take the space. Opened from the map's context menu or with Ctrl+I.
/// </summary>
public partial class InspectWindow : Window
{
    private readonly IReadOnlyList<FsNode> _nodes;
    private readonly FsNode? _root;
    private readonly SizeMeasure _measure;
    private readonly StringBuilder _copy = new();

    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _nodes = nodes.Where(n => n.IsReal).ToList();
        _root = root;
        _measure = measure;

        if (_nodes.Count == 1) BuildSingle(_nodes[0]);
        else BuildSelection();
    }

    // ---------------------------------------------------------------- one item

    private void BuildSingle(FsNode node)
    {
        Title = $"Inspect: {node.Name}";
        Glyph.Text = node.IsDirectory ? "\uE8B7" : "\uE8A5";
        NameText.Text = node.Name;
        PathText.Text = node.FullPath;
        KindText.Text = node.IsDirectory ? "Folder" : DescribeType(node);
        _copy.AppendLine(node.FullPath);

        long size = node.SizeFor(_measure);
        Fact("Size", SizeFormatter.Format(node.Size) + Exact(node.Size));
        if (node.Allocated != node.Size) Fact("On disk", SizeFormatter.Format(node.Allocated) + Exact(node.Allocated));
        if (node.IsHardLinkDuplicate) Fact("Hard link", $"{SizeFormatter.Format(node.LinkedSize)}, already counted elsewhere");

        if (node.Parent is { } parent && parent.SizeFor(_measure) > 0)
            Fact($"Share of {parent.Name}", Percent(size, parent.SizeFor(_measure)));
        if (_root is not null && !ReferenceEquals(_root, node) && _root.SizeFor(_measure) > 0)
            Fact($"Share of {_root.Name}", Percent(size, _root.SizeFor(_measure)));

        if (node.IsDirectory)
        {
            int folders = node.DescendantDirectories().Count() - 1;
            Fact("Contains", $"{node.FileCount:N0} files, {folders:N0} folders");
            Fact("Directly inside", $"{node.Children.Count(c => !c.IsDirectory && c.IsReal):N0} files, {node.Children.Count(c => c.IsDirectory):N0} folders");
            if (node.FileCount > 0) Fact("Average file", SizeFormatter.Format(node.Size / node.FileCount));
        }

        if (node.LastWriteUtc > DateTime.MinValue)
            Fact(node.IsDirectory ? "Newest change" : "Modified", DescribeDate(node.LastWriteUtc));
        if (node.AccessDenied) Fact("Note", "Some content could not be read (access denied)");
        Fact("Depth", $"{Depth(node)} levels below the scan root");

        if (node.IsDirectory)
        {
            Bars("Largest inside", node.Children.Where(c => c.IsReal && c.SizeFor(_measure) > 0).Take(8)
                .Select(c => (c.Name + (c.IsDirectory ? "\\" : string.Empty), c.SizeFor(_measure))), size);
            Bars("By file type", TypeBreakdown(node.DescendantFiles()), size);
        }
    }

    // ---------------------------------------------------------------- selection

    private void BuildSelection()
    {
        Title = $"Inspect: {_nodes.Count:N0} items";
        Glyph.Text = "\uE8B3";
        NameText.Text = $"{_nodes.Count:N0} items selected";
        var parents = _nodes.Select(n => n.Parent?.FullPath).Where(p => p is not null).Distinct().ToList();
        PathText.Text = parents.Count == 1 ? parents[0]! : $"In {parents.Count:N0} folders";
        KindText.Text = $"{_nodes.Count(n => !n.IsDirectory):N0} files, {_nodes.Count(n => n.IsDirectory):N0} folders";
        ExplorerButton.Visibility = Visibility.Collapsed;
        PropertiesButton.Visibility = Visibility.Collapsed;
        foreach (var n in _nodes) _copy.AppendLine(n.FullPath);

        long size = _nodes.Sum(n => n.SizeFor(_measure));
        long logical = _nodes.Sum(n => n.Size), allocated = _nodes.Sum(n => n.Allocated);
        Fact("Total size", SizeFormatter.Format(logical) + Exact(logical));
        if (allocated != logical) Fact("On disk", SizeFormatter.Format(allocated) + Exact(allocated));
        Fact("Files", $"{_nodes.Sum(n => n.FileCount):N0}");
        if (_root is not null && _root.SizeFor(_measure) > 0) Fact($"Share of {_root.Name}", Percent(size, _root.SizeFor(_measure)));
        var newest = _nodes.Max(n => n.LastWriteUtc);
        if (newest > DateTime.MinValue) Fact("Newest change", DescribeDate(newest));

        Bars("Items", _nodes.OrderByDescending(n => n.SizeFor(_measure)).Take(12)
            .Select(n => (n.Name + (n.IsDirectory ? "\\" : string.Empty), n.SizeFor(_measure))), size);
        Bars("By file type", TypeBreakdown(_nodes.SelectMany(n => n.IsDirectory ? n.DescendantFiles() : new[] { n })), size);
    }

    // ---------------------------------------------------------------- pieces

    private void Fact(string label, string value)
    {
        int r = Facts.RowDefinitions.Count;
        Facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = Themed(new TextBlock { Text = label, Margin = new Thickness(0, 3, 18, 3) }, TextBlock.ForegroundProperty, "TextDim");
        var v = Themed(new TextBlock { Text = value, Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "Text");
        Grid.SetRow(l, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
        Facts.Children.Add(l); Facts.Children.Add(v);
        _copy.Append(label).Append(": ").AppendLine(value);
    }

    private void Bars(string heading, IEnumerable<(string Name, long Bytes)> rows, long total)
    {
        var list = rows.Where(r => r.Bytes > 0).ToList();
        if (list.Count == 0) return;

        Sections.Children.Add(new TextBlock { Text = heading, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
        _copy.AppendLine().AppendLine(heading);
        long largest = list.Max(r => r.Bytes);

        foreach (var (name, bytes) in list)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            var n = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
            var s = Themed(new TextBlock { Text = SizeFormatter.Format(bytes), TextAlignment = TextAlignment.Right }, TextBlock.ForegroundProperty, "Text");
            var p = Themed(new TextBlock { Text = total > 0 ? $"{100.0 * bytes / total:0.#}%" : string.Empty, TextAlignment = TextAlignment.Right }, TextBlock.ForegroundProperty, "TextDim");
            Grid.SetColumn(s, 1); Grid.SetColumn(p, 2);

            var track = Themed(new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 3, 0, 0) }, Border.BackgroundProperty, "Control");
            var fill = Themed(new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left }, Border.BackgroundProperty, "Accent");
            fill.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth")
            {
                Source = track, Converter = new Fraction(largest > 0 ? (double)bytes / largest : 0)
            });
            var bar = new Grid(); bar.Children.Add(track); bar.Children.Add(fill);
            Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 3);

            grid.Children.Add(n); grid.Children.Add(s); grid.Children.Add(p); grid.Children.Add(bar);
            Sections.Children.Add(grid);
            _copy.Append("  ").Append(name).Append("  ").Append(SizeFormatter.Format(bytes)).Append("  ").AppendLine(p.Text);
        }
    }

    private IEnumerable<(string Name, long Bytes)> TypeBreakdown(IEnumerable<FsNode> files)
    {
        var sums = new Dictionary<FileCategory, long>();
        foreach (var f in files)
        {
            if (f.IsHardLinkDuplicate) continue;
            var c = Palette.Categorize(f.Extension);
            sums[c] = sums.GetValueOrDefault(c) + f.SizeFor(_measure);
        }
        return sums.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key.ToString(), kv.Value));
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Palette.Categorize(ext).ToString().ToLowerInvariant();
        return ext.Length == 0 ? $"File, no extension ({category})" : $"{ext.TrimStart('.').ToUpperInvariant()} file ({category})";
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
        return $"{local:f}  ({ago})";
    }

    private static string Exact(long bytes) => bytes >= 1024 ? $"  ({bytes:N0} bytes)" : string.Empty;
    private static string Percent(long part, long whole) => $"{100.0 * part / whole:0.##}%";
    private static int Depth(FsNode node) { int d = 0; for (var n = node.Parent; n is not null; n = n.Parent) d++; return d; }

    private static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    /// <summary>Scales a width by a fixed fraction, for the bars.</summary>
    private sealed class Fraction(double fraction) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double width ? Math.Max(0, width * fraction) : 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_copy.ToString()); } catch { /* clipboard busy */ }
    }

    private void Explorer_Click(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count != 1) return;
        try { Process.Start("explorer.exe", $"/select,\"{_nodes[0].FullPath}\""); } catch { /* shown by Explorer */ }
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count != 1) return;
        ShellProperties.Show(_nodes[0].FullPath);
    }
}
