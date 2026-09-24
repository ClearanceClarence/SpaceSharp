using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Services;
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
        ("C", "Toggle cushion shading"),
        ("G", "Toggle grouping of small items"),
        ("Esc", "Cancel a scan"),
        ("Ctrl+,", "Settings"),
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

        VersionText.Text = $"Version {_version}" + (Updater.Instance.IsInstalled ? string.Empty : "  ·  portable");
        UpdateButton.Visibility = Updater.Instance.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Checking…";
        var update = await Updater.Instance.CheckAsync();
        if (update is null)
        {
            UpdateButton.Content = "You're up to date";
            return;
        }

        UpdateButton.Content = $"Install {Updater.Instance.AvailableVersion} and restart";
        UpdateButton.IsEnabled = true;
        UpdateButton.Click -= CheckUpdates_Click;
        UpdateButton.Click += async (_, _) =>
        {
            UpdateButton.IsEnabled = false;
            try
            {
                await Updater.Instance.InstallAndRestartAsync(p => Dispatcher.BeginInvoke(() => UpdateButton.Content = $"Downloading… {p}%"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButton.IsEnabled = true;
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
