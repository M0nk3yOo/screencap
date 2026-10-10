using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Screencap.App.Tests.TestAppBuilder))]

namespace Screencap.App.Tests;

public static class TestAppBuilder
{
    // The real App class: same styles and icon resources as production. Its controller
    // only starts under a desktop lifetime, which headless tests don't have.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
