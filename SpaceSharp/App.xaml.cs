using System.Text;
using System.Windows;
using System.Windows.Threading;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class App : Application
{
    /// <summary>Folder passed on the command line, scanned as soon as the window opens.</summary>
    public static string? StartupScanPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && Directory.Exists(e.Args[0]))
            StartupScanPath = Path.GetFullPath(e.Args[0]);
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
