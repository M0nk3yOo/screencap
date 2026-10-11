using Screencap.Core.Geometry;

namespace Screencap.Core.Platform;

public static class WindowMatcher
{
    /// <summary>
    /// Best guess for the window a screenshot shows: the frontmost window under the center of
    /// the captured region. Tiny windows (tool tips, menu-bar items) are ignored.
    /// </summary>
    public static WindowInfo? Find(IReadOnlyList<WindowInfo> windowsFrontToBack, IntRect region)
    {
        var center = new PointD(region.X + region.Width / 2.0, region.Y + region.Height / 2.0);
        foreach (var window in windowsFrontToBack)
        {
            var b = window.Bounds;
            if (b.Width < 80 || b.Height < 50) continue;
            if (center.X >= b.X && center.X < b.Right && center.Y >= b.Y && center.Y < b.Bottom) return window;
        }
        return null;
    }
}
