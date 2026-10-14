using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;

namespace Screencap.App.Tests;

public sealed class BackgroundTabTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private async Task<Capture> CaptureAsync(AppController app)
    {
        await app.CaptureFullscreenAsync();
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<Capture>(app.LastCapture);
    }

    [AvaloniaFact]
    public async Task Capture_AddsTabWithoutOpeningMainWindow()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);

        var capture = await CaptureAsync(app);

        Assert.Same(capture, Assert.Single(app.Tabs));
        Assert.False(app.IsMainWindowOpen);

        // Opening the editor later (e.g. after only copying) finds the screenshot there.
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Assert.Same(capture, main.SelectedCapture);

        // "Bearbeiten" in the preview doesn't add it twice.
        app.OpenInEditor(capture);
        Assert.Single(main.OpenTabs);
    }

    [AvaloniaFact]
    public async Task Capture_KeepsCurrentTabSelectedInOpenMainWindow()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var working = new Capture(TestImages.Solid(50, 50), 1);
        app.OpenInEditor(working);
        var main = app.ShowMainWindow();
        _windows.Add(main);

        var capture = await CaptureAsync(app);

        Assert.Equal(new[] { working, capture }, main.OpenTabs);
        Assert.Same(working, main.SelectedCapture);
    }

    [AvaloniaFact]
    public async Task Capture_DropsOldestUneditedTabsBeyondLimit()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var edited = new Capture(TestImages.Solid(50, 50), 1);
        edited.Annotations.Add(new RectangleAnnotation { Rect = new RectD(1, 1, 10, 10) });
        app.Tabs.Add(edited);
        var untouched = new Capture(TestImages.Solid(50, 50), 1);
        app.Tabs.Add(untouched);
        for (var i = 2; i < AppController.MaxBackgroundTabs; i++) app.Tabs.Add(new Capture(TestImages.Solid(10, 10), 1));

        var capture = await CaptureAsync(app);

        Assert.Equal(AppController.MaxBackgroundTabs, app.Tabs.Count);
        Assert.Contains(edited, app.Tabs);
        Assert.DoesNotContain(untouched, app.Tabs);
        Assert.Same(capture, app.Tabs[^1]);
    }
}
