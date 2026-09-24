using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class AboutWindow : Window
{
    private static readonly (string Keys, string Action)[] Shortcuts =
    {
        ("Double-click / Enter", "Zoom the map to a folder"),
        ("Wheel / + / −", "Zoom in or out"),
        ("Drag", "Pan while zoomed in"),
        ("Backspace / Mouse back", "Up one folder"),
        ("Home / Ctrl+0", "Show the whole map"),
        ("F5", "Rescan"),
        ("Ctrl+C", "Copy the selected item's path"),
        ("Del", "Move the selected item to the Recycle Bin"),
        ("Esc", "Cancel a scan"),
        ("F1", "Open this window")
    };

    private readonly string _version;

    public AboutWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);

        var assembly = Assembly.GetExecutingAssembly();
        _version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "unknown";
        int plus = _version.IndexOf('+'); // the SDK appends "+<commit>" when built from git
        if (plus >= 0) _version = _version[..plus];

        VersionText.Text = $"Version {_version}";
        DescriptionText.Text = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;
        CopyrightText.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        SystemText.Text = SystemDescription();

        BuildShortcuts();
    }

    private static string SystemDescription() =>
        $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} " +
        $"({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    private void BuildShortcuts()
    {
        for (int row = 0; row < Shortcuts.Length; row++)
        {
            var (keys, action) = Shortcuts[row];
            ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caps = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in keys.Split(" / "))
            {
                var cap = new Border
                {
                    BorderThickness = new Thickness(1, 1, 1, 2),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 1, 7, 2),
                    Margin = new Thickness(0, 3, 6, 3),
                    Child = new TextBlock { Text = key, FontSize = 12 }
                };
                cap.SetResourceReference(Border.BackgroundProperty, "Control");
                cap.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                caps.Children.Add(cap);
            }

            var description = new TextBlock
            {
                Text = action,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetRow(caps, row);
            Grid.SetRow(description, row);
            Grid.SetColumn(description, 1);
            ShortcutGrid.Children.Add(caps);
            ShortcutGrid.Children.Add(description);
        }
    }

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"SpaceSharp {_version}{Environment.NewLine}{SystemDescription()}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
