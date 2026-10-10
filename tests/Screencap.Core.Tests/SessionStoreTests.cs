using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Session;

namespace Screencap.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"screencap-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void SaveAndLoad_KeepsImagesAndAnnotations()
    {
        var capture = new Capture(PixelBufferTests.Gradient(8, 6), 2.0);
        capture.Annotations.Add(new ArrowAnnotation
        {
            Start = new PointD(1, 2), End = new PointD(3, 4), Color = ArgbColor.FromUInt(0xFF007AFF), StrokeWidth = 7,
        });
        capture.Annotations.Add(new TextAnnotation { Position = new PointD(5, 5), Text = "Hallo ✓", FontSize = 30 });

        var store = new SessionStore(_directory);
        store.Save([capture]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(capture.Id, loaded.Id);
        Assert.Equal(2.0, loaded.Scaling);
        Assert.Equal(capture.Image.Data, loaded.Image.Data);
        var arrow = Assert.IsType<ArrowAnnotation>(loaded.Annotations.Items[0]);
        Assert.Equal(new PointD(3, 4), arrow.End);
        Assert.Equal(ArgbColor.FromUInt(0xFF007AFF), arrow.Color);
        Assert.Equal("Hallo ✓", Assert.IsType<TextAnnotation>(loaded.Annotations.Items[1]).Text);
        Assert.False(loaded.Annotations.IsDirty);
    }

    [Fact]
    public void Save_RemovesImagesOfClosedTabs()
    {
        var store = new SessionStore(_directory);
        var first = new Capture(PixelBufferTests.Gradient(2, 2), 1);
        var second = new Capture(PixelBufferTests.Gradient(2, 2), 1);
        store.Save([first, second]);
        store.Save([second]);

        Assert.Single(Directory.GetFiles(_directory, "*.png"));
        Assert.Equal(second.Id, Assert.Single(store.Load()).Id);
    }

    [Fact]
    public void Load_WithoutSession_IsEmpty() => Assert.Empty(new SessionStore(_directory).Load());
}
