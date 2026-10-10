using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;

namespace Screencap.App.Tests;

public sealed class TabSwitchTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private Window? _main;

    public void Dispose()
    {
        _main?.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    [AvaloniaFact]
    public void ClickingTabChip_ShowsThatEditorWithContent()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var first = new Capture(TestImages.Solid(400, 300), 1);
        var second = new Capture(TestImages.Solid(300, 200), 1);
        app.OpenInEditor(first);
        app.OpenInEditor(second);
        var main = (MainWindow)app.ShowMainWindow();
        _main = main;
        Dispatcher.UIThread.RunJobs();

        var chip = main.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("tab")).First();
        var center = chip.TranslatePoint(new Point(chip.Bounds.Width / 2 - 20, chip.Bounds.Height / 2), main)!.Value;
        main.MouseDown(center, MouseButton.Left);
        main.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(first, main.SelectedCapture);
        var visibleEditor = Assert.Single(main.GetVisualDescendants().OfType<EditorView>(), e => e.IsVisible);
        Assert.Same(first, visibleEditor.Capture);
        var canvas = visibleEditor.GetVisualDescendants().OfType<AnnotationCanvas>().Single();
        Assert.True(canvas.Bounds.Width > 50 && canvas.Bounds.Height > 50, $"canvas size {canvas.Bounds}");
        Assert.True(canvas.IsEffectivelyVisible);
    }
}

public sealed class CanvasGrowthTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private Window? _main;

    public void Dispose()
    {
        _main?.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    [AvaloniaFact]
    public void MovingInsertedImagePastEdge_GrowsCanvas()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var capture = new Capture(TestImages.Solid(400, 300), 1);
        var inserted = new Screencap.Core.Annotations.ImageAnnotation
        {
            Image = TestImages.Solid(100, 80),
            Rect = new Screencap.Core.Geometry.RectD(250, 100, 100, 80),
        };
        capture.Annotations.Add(inserted);
        app.OpenInEditor(capture);
        var main = app.ShowMainWindow();
        _main = main;
        Dispatcher.UIThread.RunJobs();
        main.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);

        var canvas = main.GetVisualDescendants().OfType<AnnotationCanvas>().Single();
        var scale = canvas.DisplayScale;
        var from = canvas.TranslatePoint(new Point(300 * scale, 140 * scale), main)!.Value;
        var to = canvas.TranslatePoint(new Point(450 * scale, 140 * scale), main)!.Value;
        main.MouseDown(from, MouseButton.Left);
        main.MouseMove(to);
        main.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(inserted.Rect.X, 395, 405);
        Assert.InRange(capture.ContentBounds.Width, 495, 505);
        Assert.Equal(capture.ContentBounds.Width, canvas.ViewRect.Width);

        using var flat = app.Flatten(capture);
        Assert.Equal(capture.ContentBounds.Width, flat.PixelSize.Width);
    }

    [AvaloniaFact]
    public void NarrowWindow_ToolbarWrapsInsteadOfClipping()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.OpenInEditor(new Capture(TestImages.Solid(400, 300), 1));
        var main = app.ShowMainWindow();
        _main = main;
        main.Width = 760;
        Dispatcher.UIThread.RunJobs();

        var copy = main.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopyButton");
        var right = copy.TranslatePoint(new Point(copy.Bounds.Width, 0), main)!.Value.X;
        Assert.True(right <= main.ClientSize.Width, $"Copy button ends at {right}, window is {main.ClientSize.Width}");

        var tools = main.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ToolPanel");
        var toolsTop = tools.TranslatePoint(default, main)!.Value.Y;
        var copyTop = copy.TranslatePoint(default, main)!.Value.Y;
        Assert.True(copyTop > toolsTop + 10, "actions moved to a second row");
    }
}

public sealed class TabReorderTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");
    private Window? _main;

    public void Dispose()
    {
        _main?.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    [AvaloniaFact]
    public void DraggingTabToTheRight_ReordersTabs()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        var a = new Capture(TestImages.Solid(100, 80), 1);
        var b = new Capture(TestImages.Solid(100, 80), 1);
        var c = new Capture(TestImages.Solid(100, 80), 1);
        app.OpenInEditor(a);
        app.OpenInEditor(b);
        app.OpenInEditor(c);
        var main = (MainWindow)app.ShowMainWindow();
        _main = main;
        Dispatcher.UIThread.RunJobs();

        var chips = main.GetVisualDescendants().OfType<Border>().Where(x => x.Classes.Contains("tab")).ToList();
        var start = chips[0].TranslatePoint(new Point(20, chips[0].Bounds.Height / 2), main)!.Value;
        var target = chips[2].TranslatePoint(new Point(chips[2].Bounds.Width - 5, chips[2].Bounds.Height / 2), main)!.Value;

        main.MouseDown(start, MouseButton.Left);
        for (var x = start.X; x <= target.X; x += 10) main.MouseMove(new Point(x, start.Y));
        main.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { b, c, a }, main.OpenTabs);
        Assert.Equal(new[] { b, c, a }, app.Tabs);
        Assert.Same(a, main.SelectedCapture);
    }
}
