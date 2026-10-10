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

public sealed class ArrowAnnotation : Annotation
{
    public PointD Start { get; set; }
    public PointD End { get; set; }

    public override RectD Bounds => RectD.FromPoints(Start, End);

    public override void Move(double dx, double dy)
    {
        Start = new PointD(Start.X + dx, Start.Y + dy);
        End = new PointD(End.X + dx, End.Y + dy);
    }

    public override bool HitTest(PointD p, double tolerance) =>
        DistanceToSegment(p, Start, End) <= tolerance + StrokeWidth;

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

    public bool Bold { get; set; } = true;
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
