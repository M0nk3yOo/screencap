using Screencap.Core.Geometry;

namespace Screencap.Core.Tests;

public class PreviewPlacementTests
{
    private static readonly RectD Screen = new(0, 0, 2000, 1200);
    private static readonly SizeD Window = new(300, 200);

    [Fact]
    public void PrefersRightOfRegion_BottomAligned()
    {
        var p = PreviewPlacement.Place(new RectD(100, 100, 400, 300), Window, Screen, 10);
        Assert.Equal(new PointD(510, 200), p);
    }

    [Fact]
    public void FallsBackToLeft_WhenNoRoomOnTheRight()
    {
        var p = PreviewPlacement.Place(new RectD(1500, 100, 400, 300), Window, Screen, 10);
        Assert.Equal(new PointD(1190, 200), p);
    }

    [Fact]
    public void FallsBackToBelow_WhenRegionSpansTheWidth()
    {
        var p = PreviewPlacement.Place(new RectD(100, 100, 1800, 300), Window, Screen, 10);
        Assert.Equal(new PointD(1600, 410), p);
    }

    [Fact]
    public void PlacesInside_ForFullScreenRegion()
    {
        var p = PreviewPlacement.Place(Screen, Window, Screen, 10);
        Assert.Equal(new PointD(1690, 990), p);
    }

    [Fact]
    public void ClampsVerticallyIntoWorkArea()
    {
        // Small region near the top: bottom alignment would push the window off screen.
        var p = PreviewPlacement.Place(new RectD(100, 20, 50, 50), Window, Screen with { Y = 40, Height = 1160 }, 10);
        Assert.Equal(new PointD(160, 40), p);
    }

    [Fact]
    public void RespectsOffsetScreens()
    {
        // Second monitor left of the primary has negative coordinates.
        var screen = new RectD(-1920, 0, 1920, 1080);
        var p = PreviewPlacement.Place(new RectD(-1800, 100, 200, 200), Window, screen, 10);
        Assert.Equal(new PointD(-1590, 100), p);
    }
}
