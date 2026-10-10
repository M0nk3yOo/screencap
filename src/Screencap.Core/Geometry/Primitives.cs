namespace Screencap.Core.Geometry;

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
}

public readonly record struct SizeD(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static RectD FromPoints(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public bool Contains(RectD r) => r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;

    public RectD Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public RectD Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
}

/// <summary>Integer rectangle, used for physical pixels.</summary>
public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public IntRect Intersect(IntRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= x || b <= y ? default : new IntRect(x, y, r - x, b - y);
    }

    public IntRect Union(IntRect other)
    {
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new IntRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }

    public static IntRect FromRectD(RectD r)
    {
        var x = (int)Math.Floor(r.X);
        var y = (int)Math.Floor(r.Y);
        return new IntRect(x, y, (int)Math.Ceiling(r.Right) - x, (int)Math.Ceiling(r.Bottom) - y);
    }
}
