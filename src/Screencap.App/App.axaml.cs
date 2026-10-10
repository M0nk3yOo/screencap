using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Screencap.App.Services;

namespace Screencap.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CrashLog.Install();
            var controller = new AppController(desktop, PlatformFactory.Create());
            controller.Start();
            desktop.ShutdownRequested += (_, _) => controller.Shutdown();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
