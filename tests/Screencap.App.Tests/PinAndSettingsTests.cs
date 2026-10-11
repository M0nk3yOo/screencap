using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;

namespace Screencap.App.Tests;

public sealed class PinAndSettingsTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    [AvaloniaFact]
    public void Pin_ClosesAfterCopying()
    {
        var platform = new FakePlatform();
        var app = new AppController(null!, platform, _dataDirectory);
        var capture = new Capture(TestImages.Solid(80, 60), 1);
        app.Pin(capture);
        var pin = Assert.Single(app.Pins);
        Dispatcher.UIThread.RunJobs();

        var copy = pin.GetVisualDescendants().OfType<Button>().First();
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(platform.ClipboardImage);
        Assert.Empty(app.Pins);
    }

    [AvaloniaFact]
    public void Settings_ShowOneSectionAtATime()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var window = new SettingsWindow(app);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string[] names = ["SectionGeneral", "SectionSaving", "SectionAppearance", "SectionHotkeys"];
        var sections = names.Select(n => window.FindControl<StackPanel>(n)!).ToArray();
        Assert.Equal([true, false, false, false], sections.Select(s => s.IsVisible));

        window.FindControl<ListBox>("Sections")!.SelectedIndex = 3;
        Assert.Equal([false, false, false, true], sections.Select(s => s.IsVisible));
        Assert.True(window.FindControl<Button>("ScrollHotkey")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Preview_ClosesAfterCopying()
    {
        var platform = new FakePlatform();
        var app = new AppController(null!, platform, _dataDirectory);
        var screen = new Window().Screens.Primary!;
        var preview = new PreviewWindow(app, new Capture(TestImages.Solid(80, 60), 1), new Avalonia.PixelRect(10, 10, 80, 60), screen);
        preview.Show();
        Dispatcher.UIThread.RunJobs();

        preview.FindControl<Button>("CopyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(platform.ClipboardImage);
        Assert.False(preview.IsVisible);
    }
}
