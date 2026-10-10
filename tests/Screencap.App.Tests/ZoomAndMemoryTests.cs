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
using Screencap.Core.Geometry;
using Screencap.Core.Platform;
using Screencap.Core.Settings;

namespace Screencap.App.Tests;

public sealed class ZoomAndMemoryTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly FakePlatform _platform = new();
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private (AppController App, MainWindow Main, AnnotationCanvas Canvas) Open(Capture capture, AppController? app = null)
    {
        app ??= new AppController(null!, _platform, _dataDirectory);
        app.OpenInEditor(capture);
        var main = (MainWindow)app.ShowMainWindow();
        _windows.Add(main);
        main.Width = 1400;
        Dispatcher.UIThread.RunJobs();
        var canvas = main.GetVisualDescendants().OfType<AnnotationCanvas>().Single(c => c.IsEffectivelyVisible);
        return (app, main, canvas);
    }

    private static T Named<T>(Window main, string name) where T : Control =>
        main.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);

    [AvaloniaFact]
    public void ZoomButtons_StepThroughZoomLevels()
    {
        var (_, main, canvas) = Open(new Capture(TestImages.Solid(400, 300), 1));
        Named<Button>(main, "ActualSizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, canvas.Zoom, 3);

        Named<Button>(main, "ZoomInButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.25, canvas.Zoom, 3);
        Named<Button>(main, "ZoomOutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Named<Button>(main, "ZoomOutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0.75, canvas.Zoom, 3);
    }

    [AvaloniaFact]
    public void CommandWheel_Zooms_PlainWheelDoesNot()
    {
        var (_, main, canvas) = Open(new Capture(TestImages.Solid(400, 300), 1));
        Named<Button>(main, "ActualSizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var point = canvas.TranslatePoint(new Point(100, 100), main)!.Value;

        main.MouseWheel(point, new Vector(0, 1));
        Assert.Equal(1, canvas.Zoom, 3);

        var command = main.CommandModifier() == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        main.MouseWheel(point, new Vector(0, 1), command);
        Assert.True(canvas.Zoom > 1.1);
    }

    [AvaloniaFact]
    public void MovingPastEdge_GrowsCanvasWhileDragging()
    {
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var rect = new RectangleAnnotation { Rect = new RectD(300, 100, 80, 60), StrokeWidth = 4 };
        capture.Annotations.Add(rect);
        var (_, main, canvas) = Open(capture);
        main.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Named<Button>(main, "ActualSizeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var s = canvas.DisplayScale;
        var from = canvas.TranslatePoint(new Point(300 * s, 130 * s), main)!.Value;
        main.MouseMove(from);
        main.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 10; i++) main.MouseMove(new Point(from.X + i * 10 * s, from.Y));
        Dispatcher.UIThread.RunJobs();

        // Still dragging: the canvas already grew and the shape followed the pointer exactly.
        Assert.InRange(rect.Rect.X, 398, 402);
        Assert.True(capture.ContentBounds.Width > 470);
        Assert.Equal(capture.ContentBounds.Width, canvas.ViewRect.Width);

        main.MouseUp(new Point(from.X + 100 * s, from.Y), MouseButton.Left);
        Assert.InRange(rect.Rect.X, 398, 402);
    }

    [AvaloniaFact]
    public async Task TextRecognitionTool_MarkedRegion_ShowsTextInBox()
    {
        _platform.OcrResult = [new RecognizedLine("Rechnung Nr. 42", new RectD(5, 5, 120, 20), [])];
        var (_, main, canvas) = Open(new Capture(TestImages.Solid(400, 300), 1));
        main.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
        Assert.Equal(EditorTool.TextRecognition, canvas.Tool);

        var s = canvas.DisplayScale;
        var a = canvas.TranslatePoint(new Point(20 * s, 20 * s), main)!.Value;
        var b = canvas.TranslatePoint(new Point(200 * s, 80 * s), main)!.Value;
        main.MouseDown(a, MouseButton.Left);
        main.MouseMove(b);
        main.MouseUp(b, MouseButton.Left);

        // OCR and the QR search run in the background.
        var box = Named<TextBox>(main, "OcrText");
        for (var i = 0; i < 200 && string.IsNullOrEmpty(box.Text); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(Named<Border>(main, "OcrPanel").IsVisible);
        Assert.Equal("Rechnung Nr. 42", box.Text);
    }

    [AvaloniaFact]
    public void ToolSettings_AreRememberedPerTool_AcrossEditors()
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        var (_, main, canvas) = Open(new Capture(TestImages.Solid(400, 300), 1), app);
        var slider = Named<Slider>(main, "SizeSlider");

        main.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        slider.Value = 9;
        main.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Equal(ToolSizes.DefaultStroke, slider.Value, 2);
        slider.Value = 2;

        // A second screenshot (new editor) starts with the remembered values.
        app.OpenInEditor(new Capture(TestImages.Solid(300, 200), 1));
        Dispatcher.UIThread.RunJobs();
        var second = main.GetVisualDescendants().OfType<AnnotationCanvas>().Single(c => c.IsEffectivelyVisible);
        main.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        Assert.Equal(9, second.StrokePoints, 2);
        main.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Equal(2, second.StrokePoints, 2);
    }

    [AvaloniaFact]
    public void TabContextMenu_OffersSaveLocation()
    {
        var capture = new Capture(TestImages.Solid(100, 80), 1);
        var (_, main, _) = Open(capture);
        var chip = main.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("tab"));
        var menu = Assert.IsType<ContextMenu>(chip.ContextMenu);
        var headers = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains("Speicherort öffnen", headers);
        Assert.Contains(headers, h => h!.StartsWith("Im ") && h.EndsWith(" zeigen"));
    }

    [AvaloniaFact]
    public void SettingsWindow_RecordsNewHotkey()
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        var settings = new SettingsWindow(app);
        _windows.Add(settings);
        settings.Show();
        Dispatcher.UIThread.RunJobs();

        settings.FindControl<Button>("OcrHotkey")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        settings.KeyPress(Key.T, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.T, "t");

        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.T), app.Settings.OcrHotkey);
    }

    [AvaloniaFact]
    public void Glass_MakesChromeTranslucent_ZeroDisablesIt()
    {
        var app = new AppController(null!, _platform, _dataDirectory);
        var (_, main, _) = Open(new Capture(TestImages.Solid(100, 80), 1), app);
        Assert.True(main.Resources.TryGetResource("ScChromeBrush", null, out var brush));
        Assert.True(((Avalonia.Media.SolidColorBrush)brush!).Color.A < 255);

        app.Settings.GlassIntensity = 0;
        app.SaveSettings();
        Assert.False(main.Resources.ContainsKey("ScChromeBrush"));
    }
}
