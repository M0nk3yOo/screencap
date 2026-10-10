using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;

namespace Screencap.App.Tests;

public sealed class EditorFlowTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private AppController CreateController() => new(null!, new FakePlatform(), _dataDirectory);

    [AvaloniaFact]
    public void OpenInEditor_ShowsMainWindowWithTab()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(400, 300), 2);

        app.OpenInEditor(capture);

        var main = Assert.IsType<MainWindow>(app.ShowMainWindow());
        _windows.Add(main);
        Assert.Same(capture, Assert.Single(main.OpenTabs));
        Assert.Same(capture, main.SelectedCapture);
        Assert.Single(main.GetVisualDescendants().OfType<EditorView>(), v => v.IsVisible);
    }

    [AvaloniaFact]
    public void EditFromPin_OpensEditorAndKeepsPin()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(120, 80), 1);
        var pin = new PinWindow(app, capture, app.Flatten(capture));
        _windows.Add(pin);
        pin.Show();

        // The toolbar button the user clicked: copy, edit, close.
        var editButton = pin.GetVisualDescendants().OfType<Button>().ElementAt(1);
        editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(app.Tabs);
        Assert.True(pin.IsVisible);
        _windows.Add(app.ShowMainWindow());
    }

    [AvaloniaFact]
    public void SecondCapture_AddsSecondTab()
    {
        var app = CreateController();
        app.OpenInEditor(new Capture(TestImages.Solid(50, 50), 1));
        app.OpenInEditor(new Capture(TestImages.Solid(60, 60), 1));
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Assert.Equal(2, main.OpenTabs.Count);
        Assert.Same(main.OpenTabs[1], main.SelectedCapture);

        app.CloseTab(main.OpenTabs[1]);
        Assert.Single(main.OpenTabs);
        Assert.Same(main.OpenTabs[0], main.SelectedCapture);
    }
}
