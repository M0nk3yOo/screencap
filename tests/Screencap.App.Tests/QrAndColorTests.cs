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
using Screencap.Core.Imaging;
using Screencap.Core.Ocr;
using ZXing;
using ZXing.Common;

namespace Screencap.App.Tests;

public sealed class QrAndColorTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private static PixelBuffer QrScreenshot(string text)
    {
        var qr = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 200, Height = 200, Margin = 2 },
        }.Write(text);
        const int size = 400;
        var data = Enumerable.Repeat((byte)255, size * size * 4).ToArray();
        for (var y = 0; y < qr.Height; y++)
            Buffer.BlockCopy(qr.Pixels, y * qr.Width * 4, data, ((y + 100) * size + 100) * 4, qr.Width * 4);
        return new PixelBuffer(size, size, data);
    }

    private (AppController App, Window Main, AnnotationCanvas Canvas) Open(Capture capture)
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        main.Width = 1400;
        Dispatcher.UIThread.RunJobs();
        return (app, main, main.GetVisualDescendants().OfType<AnnotationCanvas>().Single());
    }

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task QrButton_ShowsContentAndOffersLink()
    {
        var (_, main, _) = Open(new Capture(QrScreenshot("https://example.com/qr"), 1));
        Named<Button>(main, "QrButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var text = Named<TextBox>(main, "OcrText");
        await WaitUntil(() => !string.IsNullOrEmpty(text.Text));

        Assert.Equal("https://example.com/qr", text.Text);
        Assert.Equal("QR-Code", Named<TextBlock>(main, "OcrTitle").Text);
        Assert.True(Named<Button>(main, "OcrOpenLinkButton").IsVisible);
    }

    [AvaloniaFact]
    public async Task MarkingQrWithTextTool_ShowsCodeContent()
    {
        var (_, main, canvas) = Open(new Capture(QrScreenshot("Hallo vom QR-Code"), 1));
        main.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
        var s = canvas.DisplayScale;
        var a = canvas.TranslatePoint(new Point(80 * s, 80 * s), main)!.Value;
        var b = canvas.TranslatePoint(new Point(320 * s, 320 * s), main)!.Value;
        main.MouseDown(a, MouseButton.Left);
        main.MouseMove(b);
        main.MouseUp(b, MouseButton.Left);

        var text = Named<TextBox>(main, "OcrText");
        await WaitUntil(() => !string.IsNullOrEmpty(text.Text));
        Assert.Equal("Hallo vom QR-Code", text.Text);
        Assert.False(Named<Button>(main, "OcrOpenLinkButton").IsVisible);
    }

    [AvaloniaFact]
    public void ResultWindow_PrefersQrContent()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var capture = new Capture(QrScreenshot("x"), 1);
        var screen = new Window().Screens.Primary!;
        var window = new TextResultWindow(app, capture, "OCR-Text",
            [new DecodedCode("https://example.com", "QR-Code", default)], new PixelRect(0, 0, 100, 100), screen);
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("https://example.com", window.GetVisualDescendants().OfType<TextBox>().Single().Text);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Link öffnen" && b.IsVisible);
    }

    [AvaloniaFact]
    public void CustomColor_CanBeRemoved()
    {
        var capture = new Capture(TestImages.Solid(100, 80), 1);
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.Settings.CustomColors.Add(0xFF123456);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _windows.Add(main);
        Dispatcher.UIThread.RunJobs();

        var remove = main.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("swatchRemove"));
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(app.Settings.CustomColors);
        Assert.DoesNotContain(main.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("swatchRemove"));
    }

    [AvaloniaFact]
    public void RecognitionTools_ShareOneToolbarGroup()
    {
        var (_, main, _) = Open(new Capture(TestImages.Solid(100, 80), 1));
        var group = Named<StackPanel>(main, "RecognitionPanel");
        Assert.Equal(3, group.Children.Count);
        Assert.Contains(group.Children, c => c is Avalonia.Controls.Primitives.ToggleButton); // "Text markieren"
        Assert.Contains(group.Children, c => c.Name == "OcrButton");
        Assert.Contains(group.Children, c => c.Name == "QrButton");
        Assert.DoesNotContain(Named<StackPanel>(main, "ToolPanel").Children,
            c => ToolTip.GetTip(c) is string tip && tip.StartsWith("Text markieren"));
    }
}
