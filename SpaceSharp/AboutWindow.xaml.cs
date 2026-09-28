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
        ("Double-click / Enter", Strings.Get("Key_ZoomToFolder")),
        ("Wheel / + / −", Strings.Get("Key_ZoomInOut")),
        ("Drag", Strings.Get("Key_Pan")),
        ("Backspace / Mouse back", Strings.Get("Key_Up")),
        ("Home / Ctrl+0", Strings.Get("Key_WholeMap")),
        ("F5", Strings.Get("Key_Rescan")),
        ("Ctrl+click", Strings.Get("Key_AddSelection")),
        ("Shift+click", Strings.Get("Key_SelectRange")),
        ("Ctrl+F", Strings.Get("Key_Filter")),
        ("Ctrl+A", Strings.Get("Key_SelectMatches")),
        ("L", Strings.Get("Key_Panel")),
        ("Ctrl+C", Strings.Get("Key_Copy")),
        ("Ctrl+I", Strings.Get("Key_Inspect")),
        ("Ctrl+S", Strings.Get("Key_SaveScan")),
        ("Ctrl+O", Strings.Get("Key_OpenScan")),
        ("Alt+Enter", Strings.Get("Key_Properties")),
        ("Del", Strings.Get("Key_Delete")),
        ("S", Strings.Get("Key_NextStyle")),
        ("K", Strings.Get("Key_NextColor")),
        ("G", Strings.Get("Key_Grouping")),
        ("Esc", Strings.Get("Key_CancelScan")),
        ("Ctrl+,", Strings.Get("Key_Settings")),
        ("F1", Strings.Get("Key_About"))
    };

    private readonly string _version;

    public AboutWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);

        var assembly = Assembly.GetExecutingAssembly();
        _version = AppInfo.Version;

        VersionText.Text = Strings.Format("About_Version", _version) + (Updater.Instance.IsInstalled ? string.Empty : Strings.Get("About_Portable"));
        UpdateButton.Visibility = Updater.Instance.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        DescriptionText.Text = Strings.Get("About_Description");
        CopyrightText.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        SystemText.Text = SystemDescription();

        BuildShortcuts();
    }

    private static string SystemDescription() =>
        Strings.Format("About_SystemLine", RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription) + " " +
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
            Dialog.Error(this, Strings.Get("About_CopyFailed"), ex.Message);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = Strings.Get("About_Checking");
        var update = await Updater.Instance.CheckAsync();
        if (update is null)
        {
            UpdateButton.Content = Strings.Get("About_UpToDate");
            return;
        }

        UpdateButton.Content = Strings.Format("About_InstallAndRestart", Updater.Instance.AvailableVersion ?? string.Empty);
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
                Dialog.Error(this, Strings.Get("About_UpdateFailed"), ex.Message);
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
            Dialog.Error(this, "SpaceSharp", Strings.Format("About_BrowserFailed", url, ex.Message));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
