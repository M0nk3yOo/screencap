using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Screencap.App.Services;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.App.Rendering;

/// <summary>
/// Draws annotations in image-pixel coordinates. Used both by the live editor and for
/// export, so what you see is exactly what gets copied or saved.
/// </summary>
internal static class AnnotationRenderer
{
    private static readonly Typeface CounterTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    public static void RenderAll(DrawingContext dc, IEnumerable<Annotation> annotations, PixelBuffer source)
    {
        var items = annotations.Where(a => !a.Hidden).ToList();

        // Pixelation first: it replaces image content. Then the spotlight dims the rest
        // of the image, and everything else is drawn on top so it stays fully visible.
        foreach (var p in items.OfType<PixelateAnnotation>()) RenderPixelate(dc, p, source);
        RenderSpotlights(dc, items.OfType<SpotlightAnnotation>().ToList(), source);
        foreach (var a in items)
            if (a is not (PixelateAnnotation or SpotlightAnnotation))
                Render(dc, a);
    }

    public static void Render(DrawingContext dc, Annotation annotation)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                RenderArrow(dc, arrow);
                break;
            case RectangleAnnotation rect:
                dc.DrawRectangle(null, CreatePen(rect.Color, rect.StrokeWidth), ToRect(rect.Rect), rect.StrokeWidth, rect.StrokeWidth);
                break;
            case EllipseAnnotation ellipse:
                dc.DrawEllipse(null, CreatePen(ellipse.Color, ellipse.StrokeWidth), ToRect(ellipse.Rect));
                break;
            case HighlightAnnotation highlight:
                dc.FillRectangle(new SolidColorBrush(ToColor(highlight.Color.WithAlpha(0x60))), ToRect(highlight.Rect));
                break;
            case TextAnnotation text:
                RenderText(dc, text);
                break;
            case CounterAnnotation counter:
                RenderCounter(dc, counter);
                break;
            case ImageAnnotation image:
                RenderImage(dc, image);
                break;
        }
    }

    /// <summary>Lays out the text and stores its size on the annotation (needed for hit testing).</summary>
    public static FormattedText MeasureText(TextAnnotation text)
    {
        var formatted = new FormattedText(text.Text.Length > 0 ? text.Text : " ", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, TypefaceOf(text), text.FontSize, new SolidColorBrush(TextForeground(text)));
        if (text.BoxWidth is { } width) formatted.MaxTextWidth = Math.Max(width, text.FontSize);

        var decorations = new TextDecorationCollection();
        if (text.Underline) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (text.Strikethrough) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        if (decorations.Count > 0) formatted.SetTextDecorations(decorations);

        text.MeasuredSize = new SizeD(text.BoxWidth ?? formatted.WidthIncludingTrailingWhitespace, formatted.Height);
        return formatted;
    }

    public static Typeface TypefaceOf(TextAnnotation text) => new(
        string.IsNullOrEmpty(text.FontFamily) ? FontFamily.Default : new FontFamily(text.FontFamily),
        text.Italic ? FontStyle.Italic : FontStyle.Normal,
        text.Bold ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>Text color; on a filled box it is black or white, whichever reads better.</summary>
    public static Color TextForeground(TextAnnotation text) =>
        text.Style == TextStyle.Filled ? (text.Color.Luminance > 0.6 ? Colors.Black : Colors.White) : ToColor(text.Color);

    public static Color ToColor(ArgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static Rect ToRect(RectD r) => new(r.X, r.Y, r.Width, r.Height);

    private static Pen CreatePen(ArgbColor color, double width) =>
        new(new SolidColorBrush(ToColor(color)), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static void RenderArrow(DrawingContext dc, ArrowAnnotation arrow)
    {
        var dx = arrow.End.X - arrow.Start.X;
        var dy = arrow.End.Y - arrow.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;

        var ux = dx / length;
        var uy = dy / length;
        var headLength = Math.Min(Math.Max(arrow.StrokeWidth * 4, 12), length);
        var headHalfWidth = headLength * 0.6;
        var baseX = arrow.End.X - ux * headLength;
        var baseY = arrow.End.Y - uy * headLength;

        // The shaft ends inside the head so the round cap doesn't poke out of the tip.
        var shaftEnd = new Point(baseX + ux * headLength * 0.3, baseY + uy * headLength * 0.3);
        dc.DrawLine(CreatePen(arrow.Color, arrow.StrokeWidth), new Point(arrow.Start.X, arrow.Start.Y), shaftEnd);

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(new Point(arrow.End.X, arrow.End.Y), true);
            ctx.LineTo(new Point(baseX - uy * headHalfWidth, baseY + ux * headHalfWidth));
            ctx.LineTo(new Point(baseX + uy * headHalfWidth, baseY - ux * headHalfWidth));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(ToColor(arrow.Color)), null, head);
    }

    private static void RenderText(DrawingContext dc, TextAnnotation text)
    {
        var formatted = MeasureText(text);
        var origin = new Point(text.Position.X + text.Padding, text.Position.Y + text.Padding);
        switch (text.Style)
        {
            case TextStyle.Filled:
                var radius = text.FontSize * 0.25;
                dc.DrawRectangle(new SolidColorBrush(ToColor(text.Color)), null, ToRect(text.Bounds), radius, radius);
                break;
            case TextStyle.Outline:
                // A contrasting outline keeps text readable on any background.
                var outline = text.Color.Luminance > 0.6 ? Colors.Black : Colors.White;
                if (formatted.BuildGeometry(origin) is { } geometry)
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(outline), text.FontSize / 7, lineJoin: PenLineJoin.Round), geometry);
                break;
        }
        // DrawText (not the geometry) so underline/strikethrough are drawn too.
        dc.DrawText(formatted, origin);
    }

    private static void RenderCounter(DrawingContext dc, CounterAnnotation counter)
    {
        var center = new Point(counter.Center.X, counter.Center.Y);
        var fill = new SolidColorBrush(ToColor(counter.Color));
        var outline = new Pen(Brushes.White, counter.Radius / 7, lineJoin: PenLineJoin.Round);
        if (counter.HasVisibleTail)
            dc.DrawGeometry(fill, outline, CreateTeardrop(center, counter.Radius, counter.Tail!.Value));
        else
            dc.DrawEllipse(fill, outline, center, counter.Radius, counter.Radius);

        var foreground = counter.Color.Luminance > 0.6 ? Brushes.Black : Brushes.White;
        var label = new FormattedText(counter.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, CounterTypeface, counter.Radius * 1.1, foreground);
        dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }

    /// <summary>Circle that narrows into a point at the tail, like a map pin.</summary>
    private static StreamGeometry CreateTeardrop(Point center, double radius, PointD tail)
    {
        var angle = Math.Atan2(tail.Y - center.Y, tail.X - center.X);
        const double spread = 0.6; // radians on each side of the tail direction
        var p1 = new Point(center.X + radius * Math.Cos(angle + spread), center.Y + radius * Math.Sin(angle + spread));
        var p2 = new Point(center.X + radius * Math.Cos(angle - spread), center.Y + radius * Math.Sin(angle - spread));

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(tail.X, tail.Y), true);
        ctx.LineTo(p1);
        ctx.ArcTo(p2, new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise);
        ctx.EndFigure(true);
        return geometry;
    }

    private static readonly ConditionalWeakTable<PixelBuffer, Bitmap> ImageCache = new();

    private static void RenderImage(DrawingContext dc, ImageAnnotation image)
    {
        var bitmap = ImageCache.GetValue(image.Image, b => b.ToBitmap());
        var rect = ToRect(image.Rect);
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(bitmap, rect);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), 1), rect);
    }

    private static void RenderPixelate(DrawingContext dc, PixelateAnnotation pixelate, PixelBuffer source)
    {
        var area = IntRect.FromRectD(pixelate.Rect).Intersect(new IntRect(0, 0, source.Width, source.Height));
        if (area.IsEmpty) return;
        if (pixelate.Style == ObscureStyle.Blur)
        {
            RenderBlur(dc, area, pixelate.BlurRadius, source);
            return;
        }
        var block = Math.Max(2, pixelate.BlockSize);
        for (var y = area.Y; y < area.Bottom; y += block)
        for (var x = area.X; x < area.Right; x += block)
        {
            var cell = new IntRect(x, y, Math.Min(block, area.Right - x), Math.Min(block, area.Bottom - y));
            var color = source.Average(cell);
            // Slight overlap avoids hairline gaps from anti-aliasing at fractional zoom levels.
            dc.FillRectangle(new SolidColorBrush(ToColor(color.WithAlpha(255))),
                new Rect(cell.X, cell.Y, cell.Width + 0.5, cell.Height + 0.5));
        }
    }

    private static readonly ConditionalWeakTable<PixelBuffer, Dictionary<(IntRect, int), Bitmap>> BlurCache = new();

    /// <summary>
    /// Blurs on a downscaled copy (a strong blur hides that detail anyway) and draws it
    /// scaled up; fast enough to update live while dragging. Results are cached per area.
    /// </summary>
    private static void RenderBlur(DrawingContext dc, IntRect area, int radius, PixelBuffer source)
    {
        radius = Math.Max(1, radius);
        var factor = Math.Max(1, radius / 4);
        var margin = radius * 2;
        var expanded = new IntRect(area.X - margin, area.Y - margin, area.Width + 2 * margin, area.Height + 2 * margin)
            .Intersect(new IntRect(0, 0, source.Width, source.Height));

        var cache = BlurCache.GetOrCreateValue(source);
        if (!cache.TryGetValue((expanded, radius), out var bitmap))
        {
            var small = source.Crop(expanded).Downscale(factor);
            bitmap = small.Blur(new IntRect(0, 0, small.Width, small.Height), Math.Max(1, radius / factor)).ToBitmap();
            if (cache.Count > 24)
            {
                foreach (var old in cache.Values) old.Dispose();
                cache.Clear();
            }
            cache[(expanded, radius)] = bitmap;
        }

        using (dc.PushClip(new Rect(area.X, area.Y, area.Width, area.Height)))
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(bitmap, new Rect(expanded.X, expanded.Y, expanded.Width, expanded.Height));
    }

    private static void RenderSpotlights(DrawingContext dc, IReadOnlyList<SpotlightAnnotation> spotlights, PixelBuffer source)
    {
        if (spotlights.Count is 0) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            AddRect(ctx, new Rect(0, 0, source.Width, source.Height));
            foreach (var s in spotlights) AddRect(ctx, ToRect(s.Rect));
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), null, geometry);
    }

    private static void AddRect(StreamGeometryContext ctx, Rect r)
    {
        ctx.BeginFigure(r.TopLeft, true);
        ctx.LineTo(r.TopRight);
        ctx.LineTo(r.BottomRight);
        ctx.LineTo(r.BottomLeft);
        ctx.EndFigure(true);
    }
}
