using Avalonia;

namespace Screencap.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Lives in the menu bar like Shottr, no Dock icon.
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
