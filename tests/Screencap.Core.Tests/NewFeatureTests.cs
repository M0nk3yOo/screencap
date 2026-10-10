using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Ocr;
using Screencap.Core.Platform;
using Screencap.Core.Imaging;
using Screencap.Core.Session;

namespace Screencap.Core.Tests;

public class CropTests
{
    [Fact]
    public void Crop_IsUndoable()
    {
        var capture = new Capture(PixelBufferTests.Gradient(100, 80), 1);
        capture.Annotations.SetCrop(new IntRect(10, 10, 50, 40));
        Assert.Equal(new IntRect(10, 10, 50, 40), capture.VisibleRect);

        capture.Annotations.Undo();
        Assert.Equal(new IntRect(0, 0, 100, 80), capture.VisibleRect);
        capture.Annotations.Redo();
        Assert.Equal(new IntRect(10, 10, 50, 40), capture.VisibleRect);
    }

    [Fact]
    public void Crop_IsClampedToImage()
    {
        var capture = new Capture(PixelBufferTests.Gradient(100, 80), 1);
        capture.Annotations.SetCrop(new IntRect(80, 60, 50, 50));
        Assert.Equal(new IntRect(80, 60, 20, 20), capture.VisibleRect);
    }

    [Fact]
    public void Session_KeepsCropAndInsertedImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"screencap-crop-{Guid.NewGuid():N}");
        try
        {
            var capture = new Capture(PixelBufferTests.Gradient(40, 30), 2);
            capture.Annotations.SetCrop(new IntRect(5, 5, 20, 10));
            capture.Annotations.Add(new ImageAnnotation { Image = PixelBufferTests.Gradient(4, 3), Rect = new RectD(1, 2, 8, 6) });
            capture.Annotations.Add(new CounterAnnotation { Center = new PointD(10, 10), Number = 1, Tail = new PointD(30, 25) });

            var store = new SessionStore(directory);
            store.Save([capture]);
            var loaded = Assert.Single(store.Load());

            Assert.Equal(new IntRect(5, 5, 20, 10), loaded.Annotations.Crop);
            var image = Assert.IsType<ImageAnnotation>(loaded.Annotations.Items[0]);
            Assert.Equal(capture.Annotations.Items.OfType<ImageAnnotation>().Single().Image.Data, image.Image.Data);
            Assert.Equal(new PointD(30, 25), Assert.IsType<CounterAnnotation>(loaded.Annotations.Items[1]).Tail);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

public class CounterTailTests
{
    [Fact]
    public void Tail_ExtendsBoundsAndHitArea()
    {
        var counter = new CounterAnnotation { Center = new PointD(50, 50), Radius = 10, Tail = new PointD(100, 50) };
        Assert.True(counter.HasVisibleTail);
        Assert.Equal(100, counter.Bounds.Right);
        Assert.True(counter.HitTest(new PointD(90, 51), 1));
        Assert.False(counter.HitTest(new PointD(90, 70), 1));
    }

    [Fact]
    public void TailInsideCircle_IsIgnored()
    {
        var counter = new CounterAnnotation { Center = new PointD(50, 50), Radius = 10, Tail = new PointD(55, 50) };
        Assert.False(counter.HasVisibleTail);
        Assert.Equal(new RectD(40, 40, 20, 20), counter.Bounds);
    }

    [Fact]
    public void Move_MovesTail()
    {
        var counter = new CounterAnnotation { Center = new PointD(0, 0), Tail = new PointD(40, 0) };
        counter.Move(5, 5);
        Assert.Equal(new PointD(45, 5), counter.Tail);
    }
}

public class TextLayoutTests
{
    private static RecognizedLine Line(string text, double x, double y, double h = 20) =>
        new(text, new RectD(x, y, text.Length * 10, h), []);

    [Fact]
    public void OrdersTopToBottom()
    {
        var text = TextLayout.ToPlainText([Line("zweite", 0, 50), Line("erste", 0, 10)]);
        Assert.Equal("erste\nzweite", text);
    }

    [Fact]
    public void MergesPiecesOnTheSameLine_LeftToRight()
    {
        var text = TextLayout.ToPlainText([Line("Preis", 300, 12), Line("Artikel", 0, 10), Line("Fuß", 0, 60)]);
        Assert.Equal("Artikel  Preis\nFuß", text);
    }
}

public class ContentBoundsTests
{
    [Fact]
    public void InsertedImageOutsideScreenshot_GrowsContent()
    {
        var capture = new Capture(PixelBufferTests.Gradient(100, 80), 1);
        capture.Annotations.Add(new ImageAnnotation { Image = PixelBufferTests.Gradient(10, 10), Rect = new RectD(-20, 50, 40, 60) });

        Assert.Equal(new IntRect(-20, 0, 120, 110), capture.ContentBounds);
        Assert.Equal(capture.ContentBounds, capture.VisibleRect);
    }

    [Fact]
    public void Crop_StillLimitsVisibleArea()
    {
        var capture = new Capture(PixelBufferTests.Gradient(100, 80), 1);
        capture.Annotations.Add(new ImageAnnotation { Image = PixelBufferTests.Gradient(10, 10), Rect = new RectD(90, 70, 40, 40) });
        capture.Annotations.SetCrop(new IntRect(10, 10, 50, 50));
        Assert.Equal(new IntRect(10, 10, 50, 50), capture.VisibleRect);
    }
}

public class LayerTests
{
    [Fact]
    public void MoveTo_ReordersAndIsUndoable()
    {
        var doc = new AnnotationDocument();
        var a = new RectangleAnnotation();
        var b = new EllipseAnnotation();
        doc.Add(a);
        doc.Add(b);

        doc.MoveTo(b, 0);
        Assert.Same(b, doc.Items[0]);
        doc.Undo();
        Assert.IsType<RectangleAnnotation>(doc.Items[0]);
    }

    [Fact]
    public void HiddenAnnotations_AreNotHit()
    {
        var doc = new AnnotationDocument();
        var spot = new SpotlightAnnotation { Rect = new RectD(0, 0, 50, 50) };
        doc.Add(spot);
        doc.SetHidden(spot, true);
        Assert.Null(doc.HitTest(new PointD(10, 10), 0));
        doc.Undo();
        Assert.False(doc.Items[0].Hidden);
    }

    [Fact]
    public void TextBounds_UseBoxWidthAndPadding()
    {
        var text = new TextAnnotation { Position = new PointD(10, 10), FontSize = 20, BoxWidth = 200, Style = TextStyle.Filled };
        Assert.Equal(200 + 2 * 6, text.Bounds.Width, 3);
    }
}

public class EncodingTests
{
    [Fact]
    public void Jpeg_IsValidAndOpaque()
    {
        var transparent = new PixelBuffer(4, 4, new byte[64]);
        var jpeg = transparent.EncodeJpeg(90, new ArgbColor(255, 255, 255, 255));
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        var decoded = PixelBuffer.Decode(jpeg);
        Assert.InRange((int)decoded.GetPixel(1, 1).R, 245, 255); // transparent became white
    }

    [Fact]
    public void FileNames_ForJpegAreCleanedUpToo()
    {
        Assert.True(ScreenshotFiles.IsScreencapFile("Screencap 2026-01-01 10-00-00.jpg"));
        Assert.False(ScreenshotFiles.IsScreencapFile("Screencap 2026-01-01 10-00-00.gif"));
    }
}

public class GrowthAndMemoryTests
{
    [Fact]
    public void AnyElementPastTheEdge_GrowsContent_ButNotPixelate()
    {
        var capture = new Capture(PixelBufferTests.Gradient(100, 80), 1);
        capture.Annotations.Add(new RectangleAnnotation { Rect = new RectD(80, 60, 50, 40) });
        capture.Annotations.Add(new PixelateAnnotation { Rect = new RectD(-50, -50, 40, 40) });
        Assert.Equal(new IntRect(0, 0, 130, 100), capture.ContentBounds);
    }

    [Fact]
    public void ToolMemory_SurvivesSettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"screencap-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new Settings.SettingsStore(path);
            var settings = new Settings.AppSettings();
            settings.Tools.StrokeByTool["Arrow"] = 7.5;
            settings.Tools.Italic = true;
            settings.Tools.ObscureStyle = ObscureStyle.Blur;
            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(7.5, loaded.Tools.StrokeByTool["Arrow"]);
            Assert.True(loaded.Tools.Italic);
            Assert.False(loaded.Tools.Bold);
            Assert.Equal(ObscureStyle.Blur, loaded.Tools.ObscureStyle);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NewText_IsNotBoldByDefault() => Assert.False(new TextAnnotation().Bold);
}
