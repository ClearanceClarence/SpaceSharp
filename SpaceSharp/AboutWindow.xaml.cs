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
        ("Ctrl+click", "Add to selection"),
        ("Shift+click", "Select a range"),
        ("Ctrl+F", "Filter the map"),
        ("Ctrl+A", "Select every file matching the filter"),
        ("L", "Largest files, folders and types panel"),
        ("Ctrl+C", "Copy the selected paths"),
        ("Del", "Move the selected item to the Recycle Bin"),
        ("S", "Next map style"),
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
        // Two columns of key / action pairs.
        ShortcutGrid.ColumnDefinitions.Clear();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(24), GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            ShortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        int rows = (Shortcuts.Length + 1) / 2;
        for (int r = 0; r < rows; r++) ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < Shortcuts.Length; i++)
        {
            var (keys, action) = Shortcuts[i];
            int row = i % rows;
            int column = i < rows ? 0 : 3;

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
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5
            };

            Grid.SetRow(caps, row);
            Grid.SetColumn(caps, column);
            Grid.SetRow(description, row);
            Grid.SetColumn(description, column + 1);
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

    private const string Repository = "https://github.com/ClearanceClarence/SpaceSharp";

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(Repository);

    private void SuggestFeature_Click(object sender, RoutedEventArgs e) =>
        OpenUrl($"{Repository}/issues/new?template=feature_request.yml");

    /// <summary>Opens the bug form with the version and system fields already filled in.</summary>
    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        string install = Updater.Instance.IsInstalled ? "Setup.exe (one-click)" : "Portable SpaceSharp.exe";
        OpenUrl($"{Repository}/issues/new?template=bug_report.yml" +
                $"&version={Uri.EscapeDataString(_version)}" +
                $"&windows={Uri.EscapeDataString(SystemDescription())}" +
                $"&install={Uri.EscapeDataString(install)}");
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open the browser.\n\n{url}\n\n{ex.Message}", "SpaceSharp",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
