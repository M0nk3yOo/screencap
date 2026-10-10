using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Screencap.Core.Imaging;

namespace Screencap.App.Controls;

/// <summary>
/// Shows the frozen screen and lets the user drag out a region. A click without dragging
/// selects the whole screen. Coordinates are logical units of this control.
/// </summary>
internal sealed class RegionSelector : Control
{
    private const int LoupePixels = 15; // odd, so there is a center pixel
    private const double LoupeCellSize = 8;
    private const double LoupeOffset = 20;

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
    private static readonly IPen SelectionPen = new Pen(Brushes.White, 1);
    private static readonly IPen CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)), 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1);
    private static readonly IPen LoupeBorderPen = new Pen(Brushes.White, 2);
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    private readonly PixelBuffer _pixels;
    private Point? _pointer;
    private Point? _dragStart;

    /// <param name="imageScale">Image pixels per logical unit (2 on Retina).</param>
    public RegionSelector(Bitmap image, PixelBuffer pixels, double imageScale)
    {
        Image = image;
        _pixels = pixels;
        ImageScale = imageScale;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        ClipToBounds = true;
    }

    public double ImageScale { get; }

    public Bitmap Image { get; }

    /// <summary>Raised with the selected rectangle, or null when cancelled.</summary>
    public event Action<Rect?>? Completed;

    private Rect? Selection => _dragStart is { } s && _pointer is { } p ? new Rect(s, p).Normalize() : null;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            Completed?.Invoke(null);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        _dragStart = _pointer = Clamp(point.Position);
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _pointer = Clamp(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragStart is null) return;
        var selection = Selection;
        _dragStart = null;
        e.Pointer.Capture(null);
        // Tiny drags are accidental clicks: take the whole screen instead.
        Completed?.Invoke(selection is { Width: >= 4, Height: >= 4 } ? selection : new Rect(Bounds.Size));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Completed?.Invoke(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space)
        {
            Completed?.Invoke(new Rect(Bounds.Size));
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawImage(Image, new Rect(Image.Size), bounds);

        if (Selection is { } selection)
        {
            DrawDim(context, bounds, selection);
            context.DrawRectangle(null, SelectionPen, selection.Deflate(0.5));
            DrawSizeLabel(context, selection);
        }
        else
        {
            context.FillRectangle(DimBrush, bounds);
            if (_pointer is { } p)
            {
                context.DrawLine(CrosshairPen, new Point(0, p.Y), new Point(bounds.Width, p.Y));
                context.DrawLine(CrosshairPen, new Point(p.X, 0), new Point(p.X, bounds.Height));
            }
        }

        if (_pointer is { } pointer) DrawLoupe(context, pointer, bounds);
    }

    private static void DrawDim(DrawingContext context, Rect bounds, Rect hole)
    {
        context.FillRectangle(DimBrush, new Rect(0, 0, bounds.Width, hole.Top));
        context.FillRectangle(DimBrush, new Rect(0, hole.Bottom, bounds.Width, bounds.Height - hole.Bottom));
        context.FillRectangle(DimBrush, new Rect(0, hole.Top, hole.Left, hole.Height));
        context.FillRectangle(DimBrush, new Rect(hole.Right, hole.Top, bounds.Width - hole.Right, hole.Height));
    }

    private void DrawSizeLabel(DrawingContext context, Rect selection)
    {
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(selection.Width * ImageScale)} × {Math.Round(selection.Height * ImageScale)}");
        var position = selection.Top > 28
            ? new Point(selection.Left, selection.Top - 26)
            : new Point(selection.Left + 4, selection.Top + 4);
        DrawLabel(context, text, position);
    }

    private void DrawLoupe(DrawingContext context, Point pointer, Rect bounds)
    {
        var size = LoupePixels * LoupeCellSize;
        const double labelHeight = 24;

        // Bottom right of the cursor, flipped when it would leave the screen.
        var x = pointer.X + LoupeOffset;
        var y = pointer.Y + LoupeOffset;
        if (x + size > bounds.Width) x = pointer.X - LoupeOffset - size;
        if (y + size + labelHeight > bounds.Height) y = pointer.Y - LoupeOffset - size - labelHeight;
        var loupe = new Rect(x, y, size, size);

        var centerX = (int)Math.Floor(pointer.X * ImageScale);
        var centerY = (int)Math.Floor(pointer.Y * ImageScale);
        var half = LoupePixels / 2;
        var source = new Rect(centerX - half, centerY - half, LoupePixels, LoupePixels);

        using (context.PushClip(loupe))
        {
            context.FillRectangle(Brushes.Black, loupe);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(Image, source, loupe);
            for (var i = 1; i < LoupePixels; i++)
            {
                var offset = i * LoupeCellSize;
                context.DrawLine(GridPen, new Point(x + offset, y), new Point(x + offset, y + size));
                context.DrawLine(GridPen, new Point(x, y + offset), new Point(x + size, y + offset));
            }
        }

        var cell = new Rect(x + half * LoupeCellSize, y + half * LoupeCellSize, LoupeCellSize, LoupeCellSize);
        context.DrawRectangle(null, new Pen(Brushes.Black, 2), cell.Inflate(1));
        context.DrawRectangle(null, new Pen(Brushes.White, 1), cell);
        context.DrawRectangle(null, LoupeBorderPen, loupe);

        var color = _pixels.GetPixel(centerX, centerY);
        DrawLabel(context, $"{color.ToHex()}   {centerX}, {centerY}", new Point(x, y + size + 4), size);
    }

    private static void DrawLabel(DrawingContext context, string text, Point position, double minWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 12, Brushes.White);
        var background = new Rect(position, new Size(Math.Max(minWidth, formatted.Width + 12), formatted.Height + 6));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 20, 20, 20)), null, background, 4, 4);
        context.DrawText(formatted, new Point(position.X + 6, position.Y + 3));
    }

    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));
}
