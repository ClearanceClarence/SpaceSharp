using System.Text;
using System.Windows;
using System.Windows.Threading;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var theme = Enum.TryParse<AppTheme>(AppSettings.Current.Theme, out var saved) ? saved : AppTheme.System;
        ThemeManager.Initialize(theme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Show the whole exception chain: XAML errors hide the real cause in InnerException.
        var message = new StringBuilder();
        for (Exception? ex = e.Exception; ex is not null; ex = ex.InnerException)
        {
            if (message.Length > 0) message.AppendLine().AppendLine("Caused by:");
            message.AppendLine(ex.Message);
        }

        MessageBox.Show(message.ToString(), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
