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
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.App.Tests;

public sealed class FreehandTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private Window? _main;

    public void Dispose()
    {
        _main?.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private (AppController App, Window Main, AnnotationCanvas Canvas) Open(Capture capture)
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _main = main;
        main.Width = 1400;
        Dispatcher.UIThread.RunJobs();
        return (app, main, main.GetVisualDescendants().OfType<AnnotationCanvas>().Single());
    }

    private static void Scribble(Window main, AnnotationCanvas canvas, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var s = canvas.DisplayScale;
        Point At(double x, double y) => canvas.TranslatePoint(new Point(x * s, y * s), main)!.Value;
        main.MouseMove(At(50, 100));
        main.MouseDown(At(50, 100), MouseButton.Left, modifiers);
        for (var i = 1; i <= 30; i++)
            main.MouseMove(At(50 + i * 6, 100 + Math.Sin(i / 3.0) * 30), modifiers);
        main.MouseUp(At(230, 100), MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void Drawing_CreatesSmoothStroke_AndSelectsIt()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = Open(capture);
        main.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
        Assert.True(Named<StackPanel>(main, "PenTypePanel").IsVisible);

        Scribble(main, canvas);

        var stroke = Assert.IsType<FreehandAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.True(stroke.Points.Count > 20);
        Assert.Equal(PenType.Pen, stroke.Pen);
        Assert.Same(stroke, canvas.Selected);
        Assert.Equal(ToolSizes.DefaultFor(PenType.Pen), stroke.StrokeWidth, 2);
    }

    [AvaloniaFact]
    public void Shift_DrawsStraightLine()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = Open(capture);
        main.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);

        Scribble(main, canvas, RawInputModifiers.Shift);

        var stroke = Assert.IsType<FreehandAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Equal(2, stroke.Points.Count);
    }

    [AvaloniaFact]
    public void Highlighter_HasOwnWidth_AndIsTranslucent()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (app, main, canvas) = Open(capture);
        main.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
        Named<Avalonia.Controls.Primitives.ToggleButton>(main, "HighlighterPenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ToolSizes.DefaultFor(PenType.Highlighter), canvas.StrokePoints, 2);

        Scribble(main, canvas, RawInputModifiers.Shift);
        var stroke = Assert.IsType<FreehandAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Equal(PenType.Highlighter, stroke.Pen);

        // Background (BGR 40,120,200) shows through the red marker.
        using var flat = app.Flatten(capture);
        var pixel = flat.ToPixelBuffer().GetPixel(140, 100);
        Assert.InRange((int)pixel.R, 201, 254);
        Assert.InRange((int)pixel.B, 20, 60);

        // Back to the pen: it keeps its own width.
        Named<Avalonia.Controls.Primitives.ToggleButton>(main, "PenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        canvas.Select(null);
        Named<Avalonia.Controls.Primitives.ToggleButton>(main, "PenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ToolSizes.DefaultFor(PenType.Pen), canvas.StrokePoints, 2);
    }

    [AvaloniaFact]
    public void ExportAndClipboard_KeepRedAndBlue()
    {
        // Pure red screenshot: BGRA (0, 0, 255, 255).
        var data = new byte[30 * 20 * 4];
        for (var i = 0; i < data.Length; i += 4) { data[i + 2] = 255; data[i + 3] = 255; }
        var capture = new Capture(new PixelBuffer(30, 20, data), 1);
        var platform = new FakePlatform();
        var app = new AppController(null!, platform, _dataDirectory);

        var png = PixelBuffer.Decode(app.Encode(capture, Screencap.Core.Settings.ImageFormat.Png)).GetPixel(5, 5);
        var jpeg = PixelBuffer.Decode(app.Encode(capture, Screencap.Core.Settings.ImageFormat.Jpeg)).GetPixel(5, 5);
        Assert.True(png.R > 240 && png.B < 15, $"PNG {png}");
        Assert.True(jpeg.R > 230 && jpeg.B < 30, $"JPEG {jpeg}");

        var window = new Window();
        app.CopyAsync(capture, window).GetAwaiter().GetResult();
        var copied = platform.ClipboardImage!.GetPixel(5, 5);
        Assert.True(copied.R > 240 && copied.B < 15, $"Clipboard {copied}");
    }
}
