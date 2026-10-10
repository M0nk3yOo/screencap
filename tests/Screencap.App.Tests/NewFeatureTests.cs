using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;
using Screencap.Platform.MacOS;

namespace Screencap.App.Tests;

public sealed class NewFeatureTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];
    private readonly FakePlatform _platform = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private (AppController App, Window Main, AnnotationCanvas Canvas) OpenEditor(Capture capture)
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Dispatcher.UIThread.RunJobs();
        return (app, main, main.GetVisualDescendants().OfType<AnnotationCanvas>().Single());
    }

    private static void Drag(Window main, AnnotationCanvas canvas, Point from, Point to)
    {
        var a = canvas.TranslatePoint(from, main)!.Value;
        var b = canvas.TranslatePoint(to, main)!.Value;
        main.MouseDown(a, MouseButton.Left);
        main.MouseMove(b);
        main.MouseUp(b, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressKey(Window main, PhysicalKey key) => main.KeyPressQwerty(key, RawInputModifiers.None);

    [AvaloniaFact]
    public void CropTool_DragNewFrame_CropsAndStaysInCropTool()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = OpenEditor(capture);

        PressKey(main, PhysicalKey.C);
        Assert.Equal(EditorTool.Crop, canvas.Tool);
        var scale = canvas.DisplayScale;
        // Start outside the current frame's grab zone (the frame is the whole image).
        Drag(main, canvas, new Point(40 * scale, 30 * scale), new Point(240 * scale, 180 * scale));

        var crop = Assert.NotNull(capture.Annotations.Crop);
        Assert.InRange(crop.Width, 195, 205);
        Assert.Equal(EditorTool.Crop, canvas.Tool);

        // Enter finishes; the canvas then shows only the cropped part.
        PressKey(main, PhysicalKey.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(EditorTool.Arrow, canvas.Tool);
        Assert.Equal(crop.Width * canvas.DisplayScale, canvas.DesiredSize.Width, 1);
    }

    [AvaloniaFact]
    public void CropTool_DragOuterEdge_CropsFromThatSide()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = OpenEditor(capture);
        PressKey(main, PhysicalKey.C);
        var scale = canvas.DisplayScale;

        // Grab the right edge of the full image and pull it inwards by 100 px.
        Drag(main, canvas, new Point(398 * scale, 150 * scale), new Point(298 * scale, 150 * scale));

        var crop = Assert.NotNull(capture.Annotations.Crop);
        Assert.Equal(0, crop.X);
        Assert.Equal(0, crop.Y);
        Assert.Equal(300, crop.Height);
        Assert.InRange(crop.Width, 298, 302);

        // Then the top edge.
        Drag(main, canvas, new Point(150 * scale, 1 * scale), new Point(150 * scale, 51 * scale));
        crop = Assert.NotNull(capture.Annotations.Crop);
        Assert.InRange(crop.Y, 48, 52);
        Assert.InRange(crop.Width, 298, 302);
    }

    [AvaloniaFact]
    public void Copy_UsesNativeClipboard()
    {
        var capture = new Capture(TestImages.Solid(120, 90), 1);
        capture.Annotations.SetCrop(new IntRect(0, 0, 60, 40));
        var (app, main, _) = OpenEditor(capture);

        app.CopyAsync(capture, main).GetAwaiter().GetResult();

        var copied = _platform.ClipboardImage;
        Assert.NotNull(copied);
        Assert.Equal(60, copied.Width);
        Assert.Equal(40, copied.Height);
    }

    [AvaloniaFact]
    public void MainWindow_ShowsDockIconWhileOpen()
    {
        var (_, main, _) = OpenEditor(new Capture(TestImages.Solid(50, 50), 1));
        Assert.True(_platform.DockIconVisible);
        main.Close();
        Assert.False(_platform.DockIconVisible);
    }

    [AvaloniaFact]
    public void SizeSlider_ChangesSelectedArrow_AsOneUndoStep()
    {
        var capture = new Capture(TestImages.Solid(300, 200), 2);
        var arrow = new ArrowAnnotation { Start = new PointD(10, 10), End = new PointD(200, 100), StrokeWidth = 7 };
        capture.Annotations.Add(arrow);
        var (_, main, canvas) = OpenEditor(capture);
        PressKey(main, PhysicalKey.V);
        canvas.Select(arrow);

        var slider = main.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "SizeSlider");
        Assert.Equal(3.5, slider.Value, 1); // 7 px at 2x scaling
        slider.Value = 6;
        slider.Value = 8;

        Assert.Equal(16, arrow.StrokeWidth, 1);
        capture.Annotations.Undo();
        Assert.Equal(7, ((ArrowAnnotation)capture.Annotations.Items[0]).StrokeWidth, 1);
    }

    [AvaloniaFact]
    public void BlurStyle_RendersSmoothly()
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        var pixels = TestImages.Solid(80, 40);
        // Hard black/white edge in the middle.
        for (var y = 0; y < 40; y++)
        for (var x = 40; x < 80; x++)
        {
            var i = (y * 80 + x) * 4;
            pixels.Data[i] = pixels.Data[i + 1] = pixels.Data[i + 2] = 255;
        }
        var capture = new Capture(pixels, 1);
        capture.Annotations.Add(new PixelateAnnotation { Rect = new RectD(0, 0, 80, 40), Style = ObscureStyle.Blur, BlurRadius = 8 });

        using var flat = app.Flatten(capture);
        var result = flat.ToPixelBuffer();
        var atEdge = result.GetPixel(40, 20);
        Assert.InRange((int)atEdge.R, 60, 230); // no longer a hard edge
    }

    [AvaloniaFact]
    public void TextPadding_CoversDescenders()
    {
        var padded = AnnotationCanvas.PadText(new RectD(100, 100, 80, 20));
        Assert.True(padded.Bottom - 120 >= 10, "at least half the line height below the OCR box");
        Assert.True(padded.Y < 100 && padded.X < 100);
    }

    [AvaloniaFact]
    public void MacClipboard_ProvidesPngAndTiff()
    {
        // Overwrites the real clipboard, so it only runs on request.
        Assert.SkipUnless(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("SCREENCAP_CLIPBOARD_TEST") == "1",
            "Set SCREENCAP_CLIPBOARD_TEST=1 on macOS to run");

        new MacImageClipboard().SetImage(TestImages.Solid(64, 48));

        var info = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("osascript", "-e \"clipboard info\"")
        {
            RedirectStandardOutput = true,
        })!;
        var output = info.StandardOutput.ReadToEnd();
        info.WaitForExit();
        Assert.Contains("PNGf", output);
        Assert.Contains("TIFF", output);
        Assert.DoesNotContain("PNGf», 0", output);
    }

    [AvaloniaFact]
    public void CounterTool_DragAddsTail()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = OpenEditor(capture);

        PressKey(main, PhysicalKey.N);
        var scale = canvas.DisplayScale;
        Drag(main, canvas, new Point(50 * scale, 50 * scale), new Point(150 * scale, 120 * scale));

        var counter = Assert.IsType<CounterAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.Equal(1, counter.Number);
        Assert.True(counter.HasVisibleTail);
    }

    [AvaloniaFact]
    public void PixelateTool_ClickOnDetectedWordBlursIt()
    {
        _platform.OcrResult =
        [
            new RecognizedLine("max@example.com geheim", new RectD(20, 20, 220, 20),
            [
                new RecognizedWord("max@example.com", new RectD(20, 20, 150, 20)),
                new RecognizedWord("geheim", new RectD(180, 20, 60, 20)),
            ]),
        ];
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var (_, main, canvas) = OpenEditor(capture);

        PressKey(main, PhysicalKey.B);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(canvas.TextRegions);

        var scale = canvas.DisplayScale;
        var point = canvas.TranslatePoint(new Point(200 * scale, 30 * scale), main)!.Value;
        main.MouseMove(point);
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);

        var pixelate = Assert.IsType<PixelateAnnotation>(Assert.Single(capture.Annotations.Items));
        Assert.True(pixelate.Rect.Contains(new RectD(180, 20, 60, 20)));
        Assert.True(pixelate.Rect.X > 170, "only the clicked word, not the whole line");
    }

    [AvaloniaFact]
    public void Flatten_UsesCrop()
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        var capture = new Capture(TestImages.Solid(100, 80), 1);
        capture.Annotations.SetCrop(new IntRect(10, 10, 30, 20));
        using var flat = app.Flatten(capture);
        Assert.Equal(new PixelSize(30, 20), flat.PixelSize);
    }

    [AvaloniaFact]
    public async Task MacOcr_RecognizesRenderedText()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Apple Vision is only available on macOS");

        var pixels = RenderText("Screencap 2026", 600, 120);
        var lines = await new MacTextRecognizer().RecognizeAsync(pixels);

        var line = Assert.Single(lines);
        Assert.Contains("Screencap", line.Text);
        Assert.Equal(2, line.Words.Count);
        Assert.True(line.Words[0].Box.Right <= line.Words[1].Box.X + 2, "word boxes in reading order");
    }

    private static PixelBuffer RenderText(string text, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var dc = bitmap.CreateDrawingContext())
        {
            dc.FillRectangle(Brushes.White, new Rect(0, 0, width, height));
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 56, Brushes.Black);
            dc.DrawText(formatted, new Point(20, 25));
        }
        var data = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* p = data)
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), (IntPtr)p, data.Length, width * 4);
        }
        return new PixelBuffer(width, height, data);
    }
}
