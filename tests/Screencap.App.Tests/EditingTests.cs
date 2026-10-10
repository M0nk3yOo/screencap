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
using Screencap.Core.Settings;

namespace Screencap.App.Tests;

public sealed class EditingTests : IDisposable
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

    private static T Named<T>(Window main, string name) where T : Control =>
        main.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

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

    private static void Key(Window main, PhysicalKey key) => main.KeyPressQwerty(key, RawInputModifiers.None);

    [AvaloniaFact]
    public void OptionsPopup_OnlyForToolsWithOptions()
    {
        var (_, main, _) = Open(new Capture(TestImages.Solid(400, 300), 1));
        var bar = Named<Border>(main, "OptionsBar");
        var textOptions = Named<StackPanel>(main, "TextOptionsPanel");

        Key(main, PhysicalKey.A);
        Assert.True(bar.IsVisible);
        Assert.False(textOptions.IsVisible);

        Key(main, PhysicalKey.V);
        Assert.False(bar.IsVisible);
        Key(main, PhysicalKey.S);
        Assert.False(bar.IsVisible);

        Key(main, PhysicalKey.T);
        Assert.True(bar.IsVisible);
        Assert.True(textOptions.IsVisible);
    }

    [AvaloniaFact]
    public void NewShape_IsSelected_AndHandleResizesIt()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = Open(capture);
        Key(main, PhysicalKey.R);
        Drag(main, canvas, new Point(50, 50), new Point(150, 120));

        var rect = Assert.IsType<RectangleAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Same(rect, canvas.Selected);

        // Pull the bottom-right handle further out; works without switching tools.
        Drag(main, canvas, new Point(150, 120), new Point(250, 200));
        Assert.Single(capture.Annotations.Items);
        Assert.InRange(rect.Rect.Right, 245, 255);
        Assert.InRange(rect.Rect.Bottom, 195, 205);
        Assert.InRange(rect.Rect.X, 48, 52);
    }

    [AvaloniaFact]
    public void ArrowEndHandle_MovesOnlyThatEnd()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var arrow = new ArrowAnnotation { Start = new PointD(50, 50), End = new PointD(150, 50), StrokeWidth = 4 };
        capture.Annotations.Add(arrow);
        var (_, main, canvas) = Open(capture);
        Key(main, PhysicalKey.V);
        canvas.Select(arrow);

        Drag(main, canvas, new Point(150, 50), new Point(200, 150));
        Assert.Equal(new PointD(50, 50), arrow.Start);
        Assert.InRange(arrow.End.Y, 145, 155);
    }

    [AvaloniaFact]
    public void TextWidthHandle_SetsBoxWidth()
    {
        var capture = new Capture(TestImages.Solid(600, 300), 1);
        var text = new TextAnnotation { Position = new PointD(50, 50), Text = "Hallo Welt, ein längerer Text", FontSize = 20 };
        capture.Annotations.Add(text);
        var (_, main, canvas) = Open(capture);
        Key(main, PhysicalKey.V);
        canvas.Select(text);
        Dispatcher.UIThread.RunJobs();

        var right = text.Bounds.Right;
        var middle = text.Bounds.Y + text.Bounds.Height / 2;
        Drag(main, canvas, new Point(right, middle), new Point(150, middle));

        Assert.NotNull(text.BoxWidth);
        Assert.InRange(text.BoxWidth!.Value, 95, 105);
        Assert.True(text.MeasuredSize.Height > 30, "text wraps onto several lines");
    }

    [AvaloniaFact]
    public void BoldButton_FormatsSelectedText()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var text = new TextAnnotation { Position = new PointD(50, 50), Text = "Hallo", Bold = false };
        capture.Annotations.Add(text);
        var (_, main, canvas) = Open(capture);
        Key(main, PhysicalKey.V);
        canvas.Select(text);

        var bold = Named<Avalonia.Controls.Primitives.ToggleButton>(main, "BoldButton");
        Assert.False(bold.IsChecked);
        bold.IsChecked = true;
        bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(text.Bold);

        Named<Avalonia.Controls.Primitives.ToggleButton>(main, "FilledStyleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TextStyle.Filled, text.Style);

        capture.Annotations.Undo();
        Assert.Equal(TextStyle.Outline, ((TextAnnotation)capture.Annotations.Items[0]).Style);
    }

    [AvaloniaFact]
    public void LayersPanel_ListsAndHidesAnnotations()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        capture.Annotations.Add(new RectangleAnnotation { Rect = new RectD(10, 10, 50, 50) });
        capture.Annotations.Add(new CounterAnnotation { Center = new PointD(100, 100), Number = 1 });
        var (_, main, _) = Open(capture);

        var toggle = Named<Avalonia.Controls.Primitives.ToggleButton>(main, "LayersButton");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var list = Named<StackPanel>(main, "LayerList");
        Assert.Equal(2, list.Children.Count);
        var topRowText = list.Children[0].GetVisualDescendants().OfType<TextBlock>().First().Text;
        Assert.Equal("Nummer 1", topRowText);

        var eye = list.Children[0].GetVisualDescendants().OfType<Button>().Single();
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(capture.Annotations.Items[1].Hidden);
    }

    [AvaloniaFact]
    public void Export_TransparentBackgroundByDefault_JpegIsOpaque()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var capture = new Capture(TestImages.Solid(100, 80), 1);
        capture.Annotations.Add(new ImageAnnotation { Image = TestImages.Solid(20, 20), Rect = new RectD(150, 0, 20, 20) });

        using var flat = app.Flatten(capture);
        var pixels = flat.ToPixelBuffer();
        Assert.Equal(0, pixels.GetPixel(120, 70).A); // grown area stays transparent

        var jpeg = Screencap.Core.Imaging.PixelBuffer.Decode(app.Encode(capture, ImageFormat.Jpeg));
        Assert.Equal(255, jpeg.GetPixel(120, 70).A);
        Assert.InRange((int)jpeg.GetPixel(120, 70).R, 240, 255);
    }

    [AvaloniaFact]
    public void ColorPicker_AdoptsColor_RecolorsSelection_AndReturnsToTool()
    {
        var pixels = TestImages.Solid(200, 100); // BGRA 40,120,200 -> #C87828
        var capture = new Capture(pixels, 1);
        var arrow = new ArrowAnnotation { Start = new PointD(10, 10), End = new PointD(100, 10) };
        capture.Annotations.Add(arrow);
        var (_, main, canvas) = Open(capture);
        Key(main, PhysicalKey.A);
        canvas.Select(arrow);

        Key(main, PhysicalKey.I);
        Assert.Equal(EditorTool.ColorPicker, canvas.Tool);
        Assert.Same(arrow, canvas.Selected);

        var s = canvas.DisplayScale;
        var point = canvas.TranslatePoint(new Point(150 * s, 60 * s), main)!.Value;
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("#C87828", canvas.Color.ToHex());
        Assert.Equal("#C87828", ((ArrowAnnotation)capture.Annotations.Items[0]).Color.ToHex());
        Assert.Equal(EditorTool.Arrow, canvas.Tool);
    }

    [AvaloniaFact]
    public void OptionsPopup_HidesWhileDrawing_ReturnsOnToolChoice()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = Open(capture);
        var bar = Named<Border>(main, "OptionsBar");

        Key(main, PhysicalKey.R);
        Assert.True(bar.IsVisible);

        Drag(main, canvas, new Point(50, 50), new Point(150, 120));
        Assert.False(bar.IsVisible);

        Key(main, PhysicalKey.R);
        Assert.True(bar.IsVisible);

        // Clicking an existing shape with the select tool shows its options again.
        Drag(main, canvas, new Point(200, 200), new Point(260, 260));
        Assert.False(bar.IsVisible);
        Key(main, PhysicalKey.V);
        var s = canvas.DisplayScale;
        var edge = canvas.TranslatePoint(new Point(50 * s, 80 * s), main)!.Value;
        main.MouseDown(edge, MouseButton.Left);
        main.MouseUp(edge, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<RectangleAnnotation>(canvas.Selected);
        Assert.True(bar.IsVisible);
    }
}
