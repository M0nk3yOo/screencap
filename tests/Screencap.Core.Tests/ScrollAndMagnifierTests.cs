using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.Core.Tests;

public class ScrollStitcherTests
{
    /// <summary>A long "page" with distinct, irregular rows (like text lines of varying length).</summary>
    private static PixelBuffer Page(int width, int height)
    {
        var random = new Random(42);
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var lineLength = random.Next(width / 4, width);
            var dark = y % 7 < 3;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var v = (byte)(dark && x < lineLength ? random.Next(0, 60) : 245);
                data[i] = data[i + 1] = data[i + 2] = v;
                data[i + 3] = 255;
            }
        }
        return new PixelBuffer(width, height, data);
    }

    [Fact]
    public void StitchesScrolledFramesBackIntoThePage()
    {
        var page = Page(120, 900);
        var stitcher = new ScrollStitcher();
        int[] offsets = [0, 0, 70, 70, 190, 333, 333, 500, 600];
        foreach (var offset in offsets) stitcher.Add(page.Crop(new IntRect(0, offset, 120, 300)));

        var result = stitcher.Result();
        Assert.Equal(900, result.Height);
        Assert.Equal(page.Data, result.Data);
        Assert.Equal(6, stitcher.FrameCount); // repeated frames are skipped
    }

    [Fact]
    public void FindShift_IsZeroForUnchangedOrUnrelatedFrames()
    {
        var page = Page(100, 600);
        var frame = page.Crop(new IntRect(0, 0, 100, 200));
        Assert.Equal(0, ScrollStitcher.FindShift(frame, frame));
        Assert.Equal(45, ScrollStitcher.FindShift(frame, page.Crop(new IntRect(0, 45, 100, 200))));
    }
}

public class MagnifierTests
{
    [Fact]
    public void Lens_IsPlacedBesideSource_AndZoomFollowsItsSize()
    {
        var lens = new MagnifierAnnotation { Source = new RectD(100, 100, 40, 40) };
        lens.PlaceLens(2.5, 1000);
        Assert.Equal(100, lens.Lens.Width, 3);
        Assert.True(lens.Lens.X > lens.Source.Right);
        Assert.Equal(2.5, lens.Zoom, 3);

        lens.SetZoom(4);
        Assert.Equal(160, lens.Lens.Width, 3);
    }

    [Fact]
    public void Lens_GoesLeftWhenNoRoomOnTheRight()
    {
        var lens = new MagnifierAnnotation { Source = new RectD(900, 100, 40, 40) };
        lens.PlaceLens(2, 1000);
        Assert.True(lens.Lens.Right < lens.Source.X);
    }

    [Fact]
    public void PartsMoveIndependently()
    {
        var lens = new MagnifierAnnotation { Source = new RectD(0, 0, 20, 20), Lens = new RectD(100, 0, 40, 40) };
        lens.MoveLens(10, 5);
        Assert.Equal(new RectD(0, 0, 20, 20), lens.Source);
        Assert.Equal(new RectD(110, 5, 40, 40), lens.Lens);
        Assert.True(lens.HitLens(new PointD(130, 25), 0));
        Assert.True(lens.HitSource(new PointD(10, 10), 0));
        Assert.False(lens.HitTest(new PointD(60, 10), 0));
    }
}

public class ArrowStyleTests
{
    [Fact]
    public void CurvedArrow_PassesThroughBendPoint()
    {
        var arrow = new ArrowAnnotation { Start = new PointD(0, 0), End = new PointD(100, 0), Style = ArrowStyle.Curved };
        arrow.BendThrough(new PointD(50, 40));
        var mid = arrow.PointAt(0.5);
        Assert.Equal(50, mid.X, 3);
        Assert.Equal(40, mid.Y, 3);
        Assert.True(arrow.HitTest(new PointD(50, 40), 1));
        Assert.False(arrow.HitTest(new PointD(50, 0), 1)); // the straight line is no longer hit
        Assert.True(arrow.Bounds.Bottom >= 40);
    }

    [Fact]
    public void Move_TakesTheBendAlong()
    {
        var arrow = new ArrowAnnotation { Start = new PointD(0, 0), End = new PointD(100, 0), Style = ArrowStyle.Curved };
        arrow.ApplyDefaultBend();
        var before = arrow.PointAt(0.5);
        arrow.Move(10, 10);
        Assert.Equal(before.X + 10, arrow.PointAt(0.5).X, 3);
    }
}
