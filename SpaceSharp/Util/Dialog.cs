using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>
/// The app's own message box: same colors, type and buttons as the rest of the window instead of the
/// Windows default. <see cref="Confirm"/> asks a yes/no question, <see cref="Error"/> and <see cref="Info"/>
/// show a message with one button.
/// </summary>
public static class Dialog
{
    public static bool Confirm(Window? owner, string title, string message, string yes, string? no = null, bool danger = false) =>
        Show(owner, title, message, yes, no ?? Strings.Get("Dialog_Cancel"), danger, "\uE9CE") == true;

    public static void Error(Window? owner, string title, string message) =>
        Show(owner, title, message, Strings.Get("Dialog_Ok"), null, false, "\uEA39");

    public static void Info(Window? owner, string title, string message) =>
        Show(owner, title, message, Strings.Get("Dialog_Ok"), null, false, "\uE946");

    private static bool? Show(Window? owner, string title, string message, string primary, string? secondary, bool danger, string glyph)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            UseLayoutRounding = true,
            Icon = Application.Current?.MainWindow?.Icon
        };
        window.SetResourceReference(Control.BackgroundProperty, "Bg");
        window.SetResourceReference(Control.ForegroundProperty, "Text");
        TitleBarTheme.Attach(window);

        var app = Application.Current!;
        var icon = new TextBlock
        {
            Text = glyph, FontSize = 26, Margin = new Thickness(0, 2, 16, 0), VerticalAlignment = VerticalAlignment.Top,
            FontFamily = (FontFamily)app.FindResource("IconFont")
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Danger" : "Accent");

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var body = new DockPanel { Margin = new Thickness(24, 22, 24, 22) };
        DockPanel.SetDock(icon, Dock.Left);
        body.Children.Add(icon);
        body.Children.Add(text);

        bool? result = null;
        var yes = new Button { Content = primary, MinWidth = 96, IsDefault = true, Style = (Style)app.FindResource("AccentButton") };
        yes.Click += (_, _) => { result = true; window.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (secondary is not null)
        {
            var no = new Button { Content = secondary, MinWidth = 96, IsCancel = true, Margin = new Thickness(0, 0, 8, 0), Style = (Style)app.FindResource("ToolButton") };
            no.Click += (_, _) => { result = false; window.Close(); };
            buttons.Children.Add(no);
        }
        else yes.IsCancel = true;
        buttons.Children.Add(yes);
        if (danger) yes.SetResourceReference(Control.BackgroundProperty, "Danger");

        var footer = new Border { Padding = new Thickness(24, 12, 24, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        footer.SetResourceReference(Border.BackgroundProperty, "Panel");
        footer.SetResourceReference(Border.BorderBrushProperty, "Stroke");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        window.Content = root;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { result = secondary is null ? true : false; window.Close(); } };
        window.ShowDialog();
        return result;
    }
}
