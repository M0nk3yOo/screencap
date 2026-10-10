using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Annotations;

namespace Screencap.App.Tests;

public sealed class WindowTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private AppController CreateController() => new(null!, new FakePlatform(), _dataDirectory);

    private PreviewWindow ShowPreview(AppController app, Capture capture)
    {
        var probe = new Window();
        var screen = probe.Screens.Primary ?? probe.Screens.All.FirstOrDefault()
            ?? throw new InvalidOperationException("Headless platform reports no screen");
        var preview = new PreviewWindow(app, capture, new PixelRect(100, 100, 200, 150), screen);
        _windows.Add(preview);
        preview.Show();
        Dispatcher.UIThread.RunJobs();
        return preview;
    }

    [AvaloniaFact]
    public void Preview_EditButton_OpensTabAndClosesPreview()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(200, 150), 1);
        var preview = ShowPreview(app, capture);

        preview.FindControl<Button>("EditButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Same(capture, Assert.Single(app.Tabs));
        Assert.False(preview.IsVisible);
        _windows.Add(app.ShowMainWindow());
    }

    [AvaloniaFact]
    public void Preview_PinButton_OpensPin()
    {
        var app = CreateController();
        var preview = ShowPreview(app, new Capture(TestImages.Solid(200, 150), 1));

        preview.FindControl<Button>("PinButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var pin = Assert.Single(app.Pins);
        _windows.Add(pin);
        Assert.True(pin.IsVisible);
        Assert.False(preview.IsVisible);
    }

    [AvaloniaFact]
    public void Pin_ShowsImageAtOriginalSize()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(300, 120), 2);
        var pin = new PinWindow(app, capture, app.Flatten(capture));
        _windows.Add(pin);
        pin.SetRenderScaling(2);
        pin.Show();
        Dispatcher.UIThread.RunJobs();

        var image = pin.GetVisualDescendants().OfType<Image>().Single();
        // 300 image pixels on a 2x screen = 150 logical units = 300 physical pixels.
        Assert.Equal(150, image.Bounds.Width, 1);
        Assert.Equal(60, image.Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void Editor_DragWithArrowTool_AddsArrow()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Dispatcher.UIThread.RunJobs();

        var canvas = main.GetVisualDescendants().OfType<AnnotationCanvas>().Single();
        Assert.Equal(EditorTool.Arrow, canvas.Tool);
        var start = canvas.TranslatePoint(new Point(20, 20), main)!.Value;
        var end = canvas.TranslatePoint(new Point(120, 80), main)!.Value;

        main.MouseDown(start, MouseButton.Left);
        main.MouseMove(end);
        main.MouseUp(end, MouseButton.Left);

        var arrow = Assert.IsType<ArrowAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.True(arrow.End.X > arrow.Start.X);
        Assert.True(capture.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public void Editor_Undo_ViaShortcut()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(100, 100), 1);
        capture.Annotations.Add(new RectangleAnnotation { Rect = new(10, 10, 30, 30) });
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Dispatcher.UIThread.RunJobs();

        // Whatever the platform calls "command" (⌘ on macOS, Ctrl in the headless test platform).
        var command = main.CommandModifier() == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        main.KeyPressQwerty(PhysicalKey.Z, command);

        Assert.Empty(capture.Annotations.Items);
    }

    [AvaloniaFact]
    public void Flatten_BurnsInAnnotations()
    {
        var app = CreateController();
        var capture = new Capture(TestImages.Solid(50, 50), 1);
        capture.Annotations.Add(new HighlightAnnotation { Rect = new(0, 0, 50, 50), Color = Core.Imaging.ArgbColor.FromUInt(0xFFFFFF00) });
        using var flat = app.Flatten(capture);
        Assert.Equal(50, flat.PixelSize.Width);
    }
}
