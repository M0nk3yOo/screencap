namespace Screencap.Core.Geometry;

/// <summary>
/// Decides where the preview window appears next to a captured region.
/// All values must use the same unit (physical pixels in practice).
/// </summary>
public static class PreviewPlacement
{
    public static PointD Place(RectD region, SizeD window, RectD workArea, double gap)
    {
        // Preferred order: right, left, below, above. Side placements align to the
        // region's bottom edge, vertical placements align to its right edge.
        var right = new PointD(region.Right + gap, region.Bottom - window.Height);
        if (right.X + window.Width <= workArea.Right)
            return ClampY(right, window, workArea);

        var left = new PointD(region.X - gap - window.Width, region.Bottom - window.Height);
        if (left.X >= workArea.X)
            return ClampY(left, window, workArea);

        var below = new PointD(region.Right - window.Width, region.Bottom + gap);
        if (below.Y + window.Height <= workArea.Bottom)
            return ClampX(below, window, workArea);

        var above = new PointD(region.Right - window.Width, region.Y - gap - window.Height);
        if (above.Y >= workArea.Y)
            return ClampX(above, window, workArea);

        // Region covers (almost) the whole screen: put it inside, bottom right.
        var inside = new PointD(region.Right - gap - window.Width, region.Bottom - gap - window.Height);
        return ClampY(ClampX(inside, window, workArea), window, workArea);
    }

    private static PointD ClampX(PointD p, SizeD w, RectD area) =>
        p with { X = Math.Clamp(p.X, area.X, Math.Max(area.X, area.Right - w.Width)) };

    private static PointD ClampY(PointD p, SizeD w, RectD area) =>
        p with { Y = Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Bottom - w.Height)) };
}
