using Velopack;

namespace SpaceSharp;

/// <summary>
/// Custom entry point so Velopack can run first. It handles install/update hooks (shortcuts,
/// the first run after an update) and must be called before any WPF code executes.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
