using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.App.Tests;

public sealed class CaptureModesTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    /// <summary>Horizontal gradient: the red channel equals the x coordinate.</summary>
    private static PixelBuffer GradientX(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            data[i + 2] = (byte)x;
            data[i + 3] = 255;
        }
        return new PixelBuffer(width, height, data);
    }

    private (AppController App, Window Main, AnnotationCanvas Canvas) OpenEditor(Capture capture)
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        main.Width = 1400;
        Dispatcher.UIThread.RunJobs();
        return (app, main, main.GetVisualDescendants().OfType<AnnotationCanvas>().Single());
    }

    private static void Drag(Window main, AnnotationCanvas canvas, Point from, Point to)
    {
        var s = canvas.DisplayScale;
        var a = canvas.TranslatePoint(new Point(from.X * s, from.Y * s), main)!.Value;
        var b = canvas.TranslatePoint(new Point(to.X * s, to.Y * s), main)!.Value;
        main.MouseMove(a);
        main.MouseDown(a, MouseButton.Left);
        main.MouseMove(b);
        main.MouseUp(b, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Magnifier_MarksSpot_AndShowsItEnlargedBeside()
    {
        var capture = new Capture(GradientX(250, 250), 1);
        var (app, main, canvas) = OpenEditor(capture);
        main.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None);
        Drag(main, canvas, new Point(40, 100), new Point(80, 140));

        var lens = Assert.IsType<MagnifierAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Equal(new RectD(40, 100, 40, 40), lens.Source);
        Assert.Equal(80, lens.Lens.Width, 3); // 2× by default
        Assert.True(lens.Lens.X > lens.Source.Right, "lens sits beside the marked spot");

        // The lens center shows the spot's center (x = 60) enlarged.
        using var flat = app.Flatten(capture);
        var lensCenter = flat.ToPixelBuffer().GetPixel((int)(lens.Lens.X + lens.Lens.Width / 2), (int)(lens.Lens.Y + lens.Lens.Height / 2));
        Assert.InRange((int)lensCenter.R, 57, 63);
    }

    [AvaloniaFact]
    public void Magnifier_LensAndSpotMoveSeparately()
    {
        var capture = new Capture(GradientX(400, 300), 1);
        var lens = new MagnifierAnnotation { Source = new RectD(40, 40, 40, 40), Lens = new RectD(200, 40, 80, 80), StrokeWidth = 3 };
        capture.Annotations.Add(lens);
        var (_, main, canvas) = OpenEditor(capture);
        main.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);

        Drag(main, canvas, new Point(240, 80), new Point(240, 180)); // grab the lens
        Assert.Equal(new RectD(40, 40, 40, 40), lens.Source);
        Assert.InRange(lens.Lens.Y, 138, 142);

        Drag(main, canvas, new Point(60, 60), new Point(110, 60)); // grab the spot
        Assert.InRange(lens.Source.X, 88, 92);
        Assert.InRange(lens.Lens.Y, 138, 142);
    }

    [AvaloniaFact]
    public void ArrowStyle_AppliesToNewAndSelectedArrows()
    {
        var capture = new Capture(GradientX(400, 300), 1);
        var (_, main, canvas) = OpenEditor(capture);
        main.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        var panel = main.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ArrowStylePanel");
        Assert.True(panel.IsVisible);

        var curved = main.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Name == "ArrowCurvedButton");
        curved.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Drag(main, canvas, new Point(50, 200), new Point(250, 200));

        var arrow = Assert.IsType<ArrowAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Equal(ArrowStyle.Curved, arrow.Style);
        Assert.True(arrow.IsCurved);

        // Bend it with the middle handle.
        var bend = arrow.PointAt(0.5);
        Drag(main, canvas, new Point(bend.X, bend.Y), new Point(150, 120));
        Assert.InRange(arrow.PointAt(0.5).Y, 117, 123);

        // Switching the style of the selected arrow.
        var wedge = main.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Name == "ArrowWedgeButton");
        wedge.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ArrowStyle.Wedge, arrow.Style);

        // Rectangles don't show arrow styles.
        main.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        Assert.False(panel.IsVisible);
    }

    [AvaloniaFact]
    public void Selector_HighlightsWindow_AndClickTakesIt()
    {
        var pixels = TestImages.Solid(800, 600);
        using var bitmap = pixels.ToBitmap();
        var selector = new RegionSelector(bitmap, pixels, 1, [(new Rect(100, 80, 300, 200), "Finder – Downloads")]);
        Rect? result = null;
        selector.Completed += r => result = r;
        var window = new Window { Content = selector, Width = 800, Height = 600 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.MouseMove(new Point(200, 150));
        window.MouseDown(new Point(200, 150), MouseButton.Left);
        window.MouseUp(new Point(200, 150), MouseButton.Left);

        Assert.Equal(new Rect(100, 80, 300, 200), result);
    }

    [AvaloniaFact]
    public void Selector_DragStillSelectsRegion()
    {
        var pixels = TestImages.Solid(800, 600);
        using var bitmap = pixels.ToBitmap();
        var selector = new RegionSelector(bitmap, pixels, 1, [(new Rect(100, 80, 300, 200), "Finder")]);
        Rect? result = null;
        selector.Completed += r => result = r;
        var window = new Window { Content = selector, Width = 800, Height = 600 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.MouseMove(new Point(150, 120));
        window.MouseDown(new Point(150, 120), MouseButton.Left);
        window.MouseMove(new Point(250, 200));
        window.MouseUp(new Point(250, 200), MouseButton.Left);

        Assert.Equal(new Rect(150, 120, 100, 80), result);
    }

    [AvaloniaFact]
    public async Task ScrollCapture_StitchesFramesWhileScrolling()
    {
        // A tall page whose rows are all different; every capture is "scrolled" a bit further.
        var random = new Random(1);
        var page = new PixelBuffer(100, 700, Enumerable.Range(0, 100 * 700 * 4).Select(i => i % 4 == 3 ? (byte)255 : (byte)random.Next(256)).ToArray());
        var offset = 0;
        var platform = new FakePlatform
        {
            CaptureProvider = _ =>
            {
                var frame = page.Crop(new IntRect(0, offset, 100, 200));
                offset = Math.Min(offset + 60, 500);
                return frame;
            },
        };
        var screen = new Window().Screens.Primary!;
        var scroll = new ScrollCaptureWindow(platform, new PixelRect(screen.Bounds.X + 50, screen.Bounds.Y + 50, 100, 200), screen);
        _windows.Add(scroll);
        var run = scroll.RunAsync();

        for (var i = 0; i < 300 && offset < 500; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        scroll.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 100 && !run.IsCompleted; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        var result = Assert.IsType<PixelBuffer>(await run);
        Assert.Equal(700, result.Height);
        Assert.Equal(page.Data, result.Data);
    }

    [AvaloniaFact]
    public void Selector_ExtendsSelectionEdgesAcrossTheScreen_WhileDragging()
    {
        // Mid-grey "screen": guide lines are clearly brighter than the dimmed surroundings.
        var data = new byte[800 * 600 * 4];
        for (var i = 0; i < data.Length; i += 4) { data[i] = data[i + 1] = data[i + 2] = 128; data[i + 3] = 255; }
        var pixels = new PixelBuffer(800, 600, data);
        using var bitmap = pixels.ToBitmap();
        var selector = new RegionSelector(bitmap, pixels, 1);
        var window = new Window { Content = selector, Width = 800, Height = 600 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.MouseMove(new Point(100, 200));
        window.MouseDown(new Point(100, 200), MouseButton.Left);
        window.MouseMove(new Point(300, 350));
        var frame = window.CaptureRenderedFrame()!.ToPixelBuffer();

        // Far right of the selection, on the extension of its top edge (y = 200): a guide dash.
        var onGuide = Enumerable.Range(600, 40).Max(x => (int)frame.GetPixel(x, 200).R);
        var offGuide = frame.GetPixel(620, 260).R;
        Assert.True(onGuide > offGuide + 60, $"guide {onGuide} vs background {offGuide}");

        // Below the selection, on the extension of its right edge (x = 300).
        var onVertical = Enumerable.Range(450, 40).Max(y => (int)frame.GetPixel(300, y).R);
        Assert.True(onVertical > offGuide + 60, $"vertical guide {onVertical} vs background {offGuide}");
    }

    [AvaloniaFact]
    public void Overlay_EscapeCancels_EvenWithoutFocusOnSelector()
    {
        var pixels = new PixelBuffer(400, 300, new byte[400 * 300 * 4]);
        using var bitmap = pixels.ToBitmap();
        var screen = new Window().Screens.Primary
            ?? throw new InvalidOperationException("Headless platform reports no screen");
        var overlay = new SelectionOverlayWindow(screen, pixels, bitmap, new FakePlatform());
        _windows.Add(overlay);
        var cancelled = false;
        overlay.Selector.Completed += rect => cancelled = rect is null;
        overlay.Show();
        Dispatcher.UIThread.RunJobs();
        overlay.Focus();

        overlay.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.True(cancelled);
    }
}
