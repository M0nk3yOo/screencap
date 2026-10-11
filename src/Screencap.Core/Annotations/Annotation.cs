using System.Text.Json.Serialization;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.Core.Annotations;

/// <summary>
/// Base type of everything drawn on top of a screenshot.
/// All coordinates are in image pixels, so annotations stay exact at any zoom level.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(RectangleAnnotation), "rectangle")]
[JsonDerivedType(typeof(EllipseAnnotation), "ellipse")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(CounterAnnotation), "counter")]
[JsonDerivedType(typeof(PixelateAnnotation), "pixelate")]
[JsonDerivedType(typeof(SpotlightAnnotation), "spotlight")]
[JsonDerivedType(typeof(ImageAnnotation), "image")]
[JsonDerivedType(typeof(FreehandAnnotation), "freehand")]
[JsonDerivedType(typeof(MagnifierAnnotation), "magnifier")]
public abstract class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonConverter(typeof(ArgbColorJsonConverter))]
    public ArgbColor Color { get; set; }

    public double StrokeWidth { get; set; } = 4;

    /// <summary>Hidden via the layers panel: kept in the document, not drawn or exported.</summary>
    public bool Hidden { get; set; }

    public abstract RectD Bounds { get; }

    public abstract void Move(double dx, double dy);

    /// <summary>Hit test in image pixels; tolerance grows with the stroke so thin shapes stay clickable.</summary>
    public virtual bool HitTest(PointD p, double tolerance) => Bounds.Inflate(tolerance).Contains(p);

    /// <summary>All members are values or immutable strings, so a shallow copy is a full copy.</summary>
    public Annotation Clone() => (Annotation)MemberwiseClone();
}

/// <summary>Annotations defined by a rectangle dragged from one corner to another.</summary>
public abstract class BoxAnnotation : Annotation
{
    public RectD Rect { get; set; }

    public override RectD Bounds => Rect;

    public override void Move(double dx, double dy) => Rect = Rect.Offset(dx, dy);
}

public enum ArrowStyle
{
    /// <summary>Line with a filled triangular head.</summary>
    Standard,

    /// <summary>Line with an open "V" head.</summary>
    Open,

    /// <summary>Filled heads at both ends.</summary>
    Double,

    /// <summary>Tapered wedge: thin tail growing into the head.</summary>
    Wedge,

    /// <summary>Bent through <see cref="ArrowAnnotation.Control"/>.</summary>
    Curved,

    /// <summary>Plain line without head.</summary>
    Line,
}

public sealed class ArrowAnnotation : Annotation
{
    public PointD Start { get; set; }
    public PointD End { get; set; }

    public ArrowStyle Style { get; set; }

    /// <summary>Control point of the quadratic curve (curved style).</summary>
    public PointD? Control { get; set; }

    [JsonIgnore]
    public bool IsCurved => Style == ArrowStyle.Curved && Control is not null;

    public override RectD Bounds
    {
        get
        {
            var b = RectD.FromPoints(Start, End);
            if (!IsCurved) return b;
            // The curve stays inside the triangle start–control–end.
            var c = Control!.Value;
            var x = Math.Min(b.X, c.X);
            var y = Math.Min(b.Y, c.Y);
            return new RectD(x, y, Math.Max(b.Right, c.X) - x, Math.Max(b.Bottom, c.Y) - y);
        }
    }

    /// <summary>Point on the curve at t (0..1); a straight line unless curved.</summary>
    public PointD PointAt(double t)
    {
        if (!IsCurved) return new PointD(Start.X + (End.X - Start.X) * t, Start.Y + (End.Y - Start.Y) * t);
        var c = Control!.Value;
        var u = 1 - t;
        return new PointD(u * u * Start.X + 2 * u * t * c.X + t * t * End.X, u * u * Start.Y + 2 * u * t * c.Y + t * t * End.Y);
    }

    /// <summary>Bends the curve so it passes through <paramref name="midpoint"/> at t = 0.5.</summary>
    public void BendThrough(PointD midpoint) =>
        Control = new PointD(2 * midpoint.X - (Start.X + End.X) / 2, 2 * midpoint.Y - (Start.Y + End.Y) / 2);

    /// <summary>A pleasant default bend: sideways by a quarter of the length.</summary>
    public void ApplyDefaultBend()
    {
        var dx = End.X - Start.X;
        var dy = End.Y - Start.Y;
        var mid = new PointD((Start.X + End.X) / 2 - dy * 0.25, (Start.Y + End.Y) / 2 + dx * 0.25);
        BendThrough(mid);
    }

    public override void Move(double dx, double dy)
    {
        Start = new PointD(Start.X + dx, Start.Y + dy);
        End = new PointD(End.X + dx, End.Y + dy);
        if (Control is { } c) Control = new PointD(c.X + dx, c.Y + dy);
    }

    public override bool HitTest(PointD p, double tolerance)
    {
        if (!IsCurved) return DistanceToSegment(p, Start, End) <= tolerance + StrokeWidth;
        var previous = Start;
        for (var i = 1; i <= 24; i++)
        {
            var next = PointAt(i / 24.0);
            if (DistanceToSegment(p, previous, next) <= tolerance + StrokeWidth) return true;
            previous = next;
        }
        return false;
    }

    internal static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        var t = lengthSq == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        var cx = a.X + t * dx - p.X;
        var cy = a.Y + t * dy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}

public sealed class RectangleAnnotation : BoxAnnotation
{
    public override bool HitTest(PointD p, double tolerance)
    {
        // Only the outline is clickable, so shapes behind an empty frame stay reachable.
        var outer = Rect.Inflate(tolerance + StrokeWidth / 2);
        var inner = Rect.Inflate(-(tolerance + StrokeWidth / 2));
        return outer.Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
    }
}

public sealed class EllipseAnnotation : BoxAnnotation
{
    public override bool HitTest(PointD p, double tolerance)
    {
        var rx = Rect.Width / 2;
        var ry = Rect.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        var nx = (p.X - (Rect.X + rx)) / rx;
        var ny = (p.Y - (Rect.Y + ry)) / ry;
        var d = Math.Sqrt(nx * nx + ny * ny);
        var band = (tolerance + StrokeWidth) / Math.Min(rx, ry);
        return Math.Abs(d - 1) <= band;
    }
}

/// <summary>Translucent marker rectangle (text highlighter).</summary>
public sealed class HighlightAnnotation : BoxAnnotation;

public enum ObscureStyle
{
    /// <summary>Coarse blocks of averaged color.</summary>
    Pixelate,

    /// <summary>Smooth blur.</summary>
    Blur,
}

/// <summary>Hides content by pixelating or blurring it.</summary>
public sealed class PixelateAnnotation : BoxAnnotation
{
    public ObscureStyle Style { get; set; }

    /// <summary>Block edge length in image pixels (pixelate style).</summary>
    public int BlockSize { get; set; } = 12;

    /// <summary>Blur radius in image pixels (blur style).</summary>
    public int BlurRadius { get; set; } = 10;
}

/// <summary>Dims everything except the rectangle.</summary>
public sealed class SpotlightAnnotation : BoxAnnotation
{
    public override bool HitTest(PointD p, double tolerance) => Rect.Contains(p);
}

public enum TextStyle
{
    /// <summary>Text color with a contrasting outline, readable on any background.</summary>
    Outline,

    /// <summary>Just the text.</summary>
    Plain,

    /// <summary>Rounded box in the text color, text in a contrasting color.</summary>
    Filled,
}

public sealed class TextAnnotation : Annotation
{
    public PointD Position { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 28;

    /// <summary>Font family name; null = the app's default font.</summary>
    public string? FontFamily { get; set; }

    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public TextStyle Style { get; set; }

    /// <summary>Fixed box width in image pixels (text wraps inside); null = as wide as the text.</summary>
    public double? BoxWidth { get; set; }

    /// <summary>Measured size, set by the UI after layout (fonts are a UI concern).</summary>
    [JsonIgnore]
    public SizeD MeasuredSize { get; set; }

    /// <summary>Inner padding of the <see cref="TextStyle.Filled"/> box.</summary>
    [JsonIgnore]
    public double Padding => Style == TextStyle.Filled ? FontSize * 0.3 : 0;

    public override RectD Bounds => new(Position.X, Position.Y,
        (BoxWidth ?? Math.Max(MeasuredSize.Width, FontSize)) + 2 * Padding,
        Math.Max(MeasuredSize.Height, FontSize) + 2 * Padding);

    public override void Move(double dx, double dy) => Position = new PointD(Position.X + dx, Position.Y + dy);
}

/// <summary>Numbered circle for step-by-step instructions, optionally with a pointer tail.</summary>
public sealed class CounterAnnotation : Annotation
{
    public PointD Center { get; set; }
    public int Number { get; set; }
    public double Radius { get; set; } = 18;

    /// <summary>Tip of the pointer, set by dragging while placing the number. Null = plain circle.</summary>
    public PointD? Tail { get; set; }

    /// <summary>The tail is only drawn when it reaches outside the circle.</summary>
    [JsonIgnore]
    public bool HasVisibleTail => Tail is { } t && Distance(Center, t) > Radius * 1.2;

    public override RectD Bounds
    {
        get
        {
            var circle = new RectD(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
            if (!HasVisibleTail) return circle;
            var t = Tail!.Value;
            var x = Math.Min(circle.X, t.X);
            var y = Math.Min(circle.Y, t.Y);
            return new RectD(x, y, Math.Max(circle.Right, t.X) - x, Math.Max(circle.Bottom, t.Y) - y);
        }
    }

    public override void Move(double dx, double dy)
    {
        Center = new PointD(Center.X + dx, Center.Y + dy);
        if (Tail is { } t) Tail = new PointD(t.X + dx, t.Y + dy);
    }

    public override bool HitTest(PointD p, double tolerance) =>
        Distance(p, Center) <= Radius + tolerance ||
        (HasVisibleTail && ArrowAnnotation.DistanceToSegment(p, Center, Tail!.Value) <= tolerance + Radius / 3);

    private static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>A screenshot pasted into another one ("insert capture").</summary>
public sealed class ImageAnnotation : BoxAnnotation
{
    [JsonConverter(typeof(PixelBufferJsonConverter))]
    public PixelBuffer Image { get; set; } = null!;

    public override bool HitTest(PointD p, double tolerance) => Rect.Inflate(tolerance).Contains(p);
}

public enum PenType
{
    /// <summary>Opaque round line — notes, signatures, circling things.</summary>
    Pen,

    /// <summary>Wide translucent stroke; overlaps within one stroke don't get darker.</summary>
    Highlighter,

    /// <summary>Colored line with a contrasting rim, visible on any background.</summary>
    Outline,
}

/// <summary>A freehand stroke. The point list is never changed in place, so clones may share it.</summary>
public sealed class FreehandAnnotation : Annotation
{
    public IReadOnlyList<PointD> Points { get; set; } = [];

    public PenType Pen { get; set; }

    public override RectD Bounds
    {
        get
        {
            if (Points.Count == 0) return default;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in Points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }
            return new RectD(minX, minY, maxX - minX, maxY - minY).Inflate(StrokeWidth / 2);
        }
    }

    public override void Move(double dx, double dy) =>
        Points = Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToList();

    public override bool HitTest(PointD p, double tolerance)
    {
        var reach = tolerance + StrokeWidth / 2;
        if (Points.Count == 1)
            return Math.Abs(p.X - Points[0].X) <= reach && Math.Abs(p.Y - Points[0].Y) <= reach;
        for (var i = 1; i < Points.Count; i++)
            if (ArrowAnnotation.DistanceToSegment(p, Points[i - 1], Points[i]) <= reach) return true;
        return false;
    }
}

/// <summary>
/// Magnified detail: an oval marks the spot (<see cref="Source"/>), and a round lens next to it
/// (<see cref="Lens"/>) shows it enlarged, connected by two lines. Both parts move independently.
/// </summary>
public sealed class MagnifierAnnotation : Annotation
{
    public RectD Source { get; set; }
    public RectD Lens { get; set; }

    [JsonIgnore]
    public double Zoom => Source.Width > 0 ? Lens.Width / Source.Width : 2;

    public override RectD Bounds
    {
        get
        {
            var a = Source.Inflate(StrokeWidth);
            var b = Lens.Inflate(StrokeWidth);
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            return new RectD(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
        }
    }

    public override void Move(double dx, double dy)
    {
        MoveSource(dx, dy);
        MoveLens(dx, dy);
    }

    public void MoveSource(double dx, double dy) => Source = Source.Offset(dx, dy);

    public void MoveLens(double dx, double dy) => Lens = Lens.Offset(dx, dy);

    /// <summary>Resizes the lens around its center so it shows the source at <paramref name="zoom"/>.</summary>
    public void SetZoom(double zoom)
    {
        var cx = Lens.X + Lens.Width / 2;
        var cy = Lens.Y + Lens.Height / 2;
        var w = Source.Width * zoom;
        var h = Source.Height * zoom;
        Lens = new RectD(cx - w / 2, cy - h / 2, w, h);
    }

    /// <summary>Puts the lens beside the source (right, or left if there is no room).</summary>
    public void PlaceLens(double zoom, double imageWidth)
    {
        var w = Source.Width * zoom;
        var h = Source.Height * zoom;
        var gap = Math.Max(Source.Width, Source.Height) * 0.6;
        var x = Source.Right + gap;
        if (x + w > imageWidth && Source.X - gap - w >= 0) x = Source.X - gap - w;
        var y = Source.Y + Source.Height / 2 - h / 2 - h * 0.25;
        Lens = new RectD(x, y, w, h);
    }

    public bool HitLens(PointD p, double tolerance) => InEllipse(Lens.Inflate(tolerance), p);

    public bool HitSource(PointD p, double tolerance) => InEllipse(Source.Inflate(tolerance + StrokeWidth), p);

    public override bool HitTest(PointD p, double tolerance) => HitLens(p, tolerance) || HitSource(p, tolerance);

    private static bool InEllipse(RectD r, PointD p)
    {
        if (r.Width <= 0 || r.Height <= 0) return false;
        var nx = (p.X - (r.X + r.Width / 2)) / (r.Width / 2);
        var ny = (p.Y - (r.Y + r.Height / 2)) / (r.Height / 2);
        return nx * nx + ny * ny <= 1;
    }
}
