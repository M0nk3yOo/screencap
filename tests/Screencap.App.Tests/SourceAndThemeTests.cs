using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Geometry;
using Screencap.Core.Platform;
using Screencap.Core.Settings;

namespace Screencap.App.Tests;

public sealed class SourceAndThemeTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    [AvaloniaFact]
    public async Task FullscreenCapture_RemembersFrontWindow()
    {
        var platform = new FakePlatform();
        var screen = new Window().Screens.Primary!;
        var b = screen.Bounds;
        platform.Windows_ =
        [
            new WindowInfo("Safari", "Screencap – Doku", new IntRect(b.X, b.Y, b.Width, b.Height)),
            new WindowInfo("Finder", "Downloads", new IntRect(b.X, b.Y, b.Width, b.Height)),
        ];
        var app = new AppController(null!, platform, _dataDirectory);

        await app.CaptureFullscreenAsync();
        Dispatcher.UIThread.RunJobs();

        var capture = Assert.IsType<Capture>(app.LastCapture);
        Assert.Equal("Safari", capture.SourceApp);
        Assert.Equal("Screencap – Doku", capture.SourceTitle);
        Assert.Equal("Safari – Screencap – Doku", capture.SourceDescription);
    }

    [AvaloniaFact]
    public void SavedFiles_CarryAppAndWindowTitle()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var capture = new Capture(TestImages.Solid(40, 30), 1) { SourceApp = "Mail", SourceTitle = "Rechnung März" };

        var png = Encoding.UTF8.GetString(app.Encode(capture, ImageFormat.Png));
        Assert.Contains("Rechnung März", png);
        Assert.Contains("Screencap:Application", png);

        var jpeg = app.Encode(capture, ImageFormat.Jpeg);
        Assert.Contains(Encoding.Latin1.GetString(Encoding.Unicode.GetBytes("Rechnung März")), Encoding.Latin1.GetString(jpeg));
    }

    [AvaloniaFact]
    public void FirstStart_EnablesAutostartOnce()
    {
        var platform = new FakePlatform();
        new AppController(null!, platform, _dataDirectory).Start();
        Assert.True(platform.IsEnabled);

        // The user turns it off; later starts must respect that.
        platform.SetEnabled(false);
        var again = new AppController(null!, platform, _dataDirectory);
        again.Start();
        Assert.False(platform.IsEnabled);
        Assert.True(again.Settings.AutostartConfigured);
    }

    [AvaloniaFact]
    public void ThemeSetting_SwitchesBetweenLightDarkAndSystem()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var settings = new SettingsWindow(app);
        settings.Show();
        var combo = settings.FindControl<ComboBox>("ThemeMode")!;

        combo.SelectedIndex = 2;
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal(AppTheme.Dark, app.Settings.Theme);

        combo.SelectedIndex = 1;
        Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);

        combo.SelectedIndex = 0;
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        settings.Close();
    }
}
