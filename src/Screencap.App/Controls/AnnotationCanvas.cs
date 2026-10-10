using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Screencap.App.Rendering;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.App.Controls;

/// <summary>
/// Displays a screenshot with its annotations and turns pointer input into annotations.
/// Internally everything is in image pixels; <see cref="DisplayScale"/> maps to the screen
/// and <see cref="ViewRect"/> is the part of the image that is shown (the crop).
/// </summary>
internal sealed class AnnotationCanvas : Control
{
    private const double MinShapeSize = 3;
    private const double MinCropSize = 8;

    private static readonly IBrush CropDimBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x99, 0, 0, 0));
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    private readonly Capture _capture;
    private readonly Bitmap _image;
    private Annotation? _draft;
    private RectD? _cropDraft;
    private PointD _dragStart;
    private PointD _lastPoint;
    private bool _moving;
    private bool _moveRecorded;
    private double _zoom = 1;
    private EditorTool _tool = EditorTool.Arrow;
    private IReadOnlyList<RecognizedLine>? _textRegions;
    private RectD? _hoverText;
    private RectD? _pressedText;
    private CropDrag _cropDrag;
    private RectD _cropStart;
    private Annotation? _styleEditTarget;
    private RectD? _ocrDraft;
    private Point _dragStartScreen;
    private Point _lastScreen;
    private IntRect _lastView;
    private bool _dragGrowing;
    private string? _activeHandle;
    private Annotation? _handleOriginal;
    private bool _handleRecorded;
    private Cursor _toolCursor = Cursor.Default;
    private const double HandleSize = 9;

    public AnnotationCanvas(Capture capture, Bitmap image)
    {
        _capture = capture;
        _image = image;
        Focusable = true;
        ClipToBounds = true;
        capture.Annotations.Changed += (_, _) =>
        {
            InvalidateMeasure();
            InvalidateVisual();
        };
    }

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            _cropDraft = null;
            // The crop tool shows the whole image so the crop can also be enlarged again.
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public ArgbColor Color { get; set; } = ArgbColor.FromUInt(0xFFFF3B30);

    /// <summary>Fill for canvas area outside the screenshot; alpha 0 shows a checkerboard.</summary>
    public ArgbColor CanvasBackground { get; set; }

    /// <summary>Formatting for new text (font, bold, style …); the text itself is ignored.</summary>
    public TextAnnotation TextDefaults { get; } = new();

    /// <summary>Cursor of the active tool; handles temporarily override it.</summary>
    public Cursor ToolCursor
    {
        get => _toolCursor;
        set
        {
            _toolCursor = value;
            Cursor = value;
        }
    }

    /// <summary>OCR result used by the pixelate tool to blur text with a single click.</summary>
    public IReadOnlyList<RecognizedLine>? TextRegions
    {
        get => _textRegions;
        set
        {
            _textRegions = value;
            _hoverText = null;
            InvalidateVisual();
        }
    }

    /// <summary>Line width in points; also drives text size and counter radius.</summary>
    public double StrokePoints { get; set; } = ToolSizes.DefaultStroke;

    /// <summary>How strongly the pixelate/blur tool obscures (1–10).</summary>
    public double ObscureStrength { get; set; } = ToolSizes.DefaultStrength;

    public ObscureStyle ObscureStyle { get; set; } = ObscureStyle.Pixelate;

    public Annotation? Selected { get; private set; }

    /// <summary>Text currently open in the inline editor; hidden from rendering meanwhile.</summary>
    public TextAnnotation? EditingText { get; set; }

    /// <summary>1 = one image pixel per physical screen pixel.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Logical units per image pixel.</summary>
    public double DisplayScale => Zoom / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    /// <summary>
    /// The area currently displayed, in image pixels. Frozen while dragging, so a growing
    /// canvas (inserted image moved past the edge) doesn't shift the image under the pointer.
    /// </summary>
    public IntRect ViewRect => Tool == EditorTool.Crop ? _capture.ContentBounds : _capture.VisibleRect;

    /// <summary>Request to open the inline text editor at an image position (existing text or null for new).</summary>
    public event Action<PointD, TextAnnotation?>? TextEditRequested;

    public event Action<ArgbColor>? ColorPicked;

    /// <summary>Image position under the pointer, null when it leaves the canvas.</summary>
    public event Action<PointD?>? PointerImagePositionChanged;

    public event Action? SelectionChanged;

    /// <summary>Moving or resizing an existing annotation started/ended (the canvas may grow meanwhile).</summary>
    public event Action? DragStarted;

    public event Action? DragEnded;

    /// <summary>
    /// The visible area's origin moved by (dx, dy) image pixels during a drag, because the
    /// canvas grew to the left/top. The host shifts the canvas so nothing jumps under the pointer.
    /// </summary>
    public event Action<double, double>? ViewOriginShifted;

    /// <summary>A region was marked with the text recognition tool (image pixels).</summary>
    public event Action<IntRect>? TextRegionSelected;

    /// <summary>The user started changing the image (drawing, moving, resizing).</summary>
    public event Action? EditingStarted;

    /// <summary>An annotation was clicked with the select tool (also when it was already selected).</summary>
    public event Action? AnnotationClicked;

    public void Select(Annotation? annotation)
    {
        Selected = annotation;
        _styleEditTarget = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (Selected is null) return;
        _capture.Annotations.Remove(Selected);
        Select(null);
    }

    /// <summary>Recolors the selected annotation, so picking a swatch also edits the selection.</summary>
    public void ApplyColorToSelection()
    {
        if (Selected is null || Selected is PixelateAnnotation or SpotlightAnnotation or ImageAnnotation) return;
        _capture.Annotations.BeginChange();
        Selected.Color = Color;
        _capture.Annotations.NotifyChanged();
    }

    /// <summary>
    /// Applies the slider values to the selected annotation. Consecutive changes to the same
    /// annotation (a slider drag) form a single undo step.
    /// </summary>
    public void ApplyStyleToSelection()
    {
        if (Selected is not { } target) return;
        if (_styleEditTarget != target)
        {
            _capture.Annotations.BeginChange();
            _styleEditTarget = target;
        }
        var scaling = _capture.Scaling;
        switch (target)
        {
            case PixelateAnnotation p:
                p.Style = ObscureStyle;
                p.BlockSize = (int)Math.Round(ToolSizes.BlockSizeFor(ObscureStrength) * scaling);
                p.BlurRadius = (int)Math.Round(ToolSizes.BlurRadiusFor(ObscureStrength) * scaling);
                break;
            case TextAnnotation t:
                t.FontSize = ToolSizes.FontFor(StrokePoints) * scaling;
                break;
            case CounterAnnotation c:
                c.Radius = ToolSizes.CounterRadiusFor(StrokePoints) * scaling;
                break;
            case ArrowAnnotation or RectangleAnnotation or EllipseAnnotation:
                target.StrokeWidth = StrokePoints * scaling;
                break;
            default:
                return;
        }
        _capture.Annotations.NotifyChanged();
    }

    /// <summary>Edits the selected annotation as one undo step (toggles, combo boxes).</summary>
    public void EditSelected<T>(Action<T> change) where T : Annotation
    {
        if (Selected is not T target) return;
        _capture.Annotations.BeginChange();
        _styleEditTarget = null;
        change(target);
        _capture.Annotations.NotifyChanged();
    }

    public Point ImageToCanvas(PointD p)
    {
        var view = ViewRect;
        return new Point((p.X - view.X) * DisplayScale, (p.Y - view.Y) * DisplayScale);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var view = ViewRect;
        return new Size(view.Width * DisplayScale, view.Height * DisplayScale);
    }

    public override void Render(DrawingContext context)
    {
        var scale = DisplayScale;
        var view = ViewRect;
        var imageRect = new Rect(0, 0, _capture.Image.Width, _capture.Image.Height);
        var content = _capture.ContentBounds;
        var contentRect = new Rect(content.X, content.Y, content.Width, content.Height);
        using (context.PushTransform(Matrix.CreateTranslation(-view.X, -view.Y) * Matrix.CreateScale(scale, scale)))
        {
            // Area gained by moving inserted images past the screenshot's edge.
            if (contentRect != imageRect) RenderCanvasBackground(context, contentRect, scale);

            // Crisp pixels when zoomed in, smooth downscaling when zoomed out.
            var interpolation = Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation }))
                context.DrawImage(_image, imageRect);

            using (context.PushClip(contentRect))
            {
                var items = _capture.Annotations.Items.Where(a => a != EditingText);
                if (_draft is not null) items = items.Append(_draft);
                AnnotationRenderer.RenderAll(context, items, _capture.Image);
            }

            if (Tool == EditorTool.Pixelate && _draft is null) RenderTextRegions(context, scale);
            if (_ocrDraft is { } ocrRect)
            {
                var r = AnnotationRenderer.ToRect(ocrRect);
                context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x26, 0, 122, 255)),
                    new Pen(new SolidColorBrush(Colors.DodgerBlue), 2 / scale, new DashStyle([4, 3], 0)), r);
            }
            if (Tool == EditorTool.Crop) RenderCropOverlay(context, contentRect, scale);
            else if (Selected is not null && Selected != EditingText) RenderSelection(context, Selected, scale);
        }
    }

    private void RenderCanvasBackground(DrawingContext context, Rect area, double scale)
    {
        if (CanvasBackground.A > 0)
        {
            context.FillRectangle(new SolidColorBrush(AnnotationRenderer.ToColor(CanvasBackground)), area);
            return;
        }
        // Transparent: the usual checkerboard, with a constant on-screen tile size.
        context.FillRectangle(Brushes.White, area);
        var tile = 8 / scale;
        var light = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD8, 0xD8, 0xDC));
        using (context.PushClip(area))
        {
            var startX = Math.Floor(area.X / tile);
            var startY = Math.Floor(area.Y / tile);
            for (var y = startY; y * tile < area.Bottom; y++)
            for (var x = startX + ((long)(y - startY) % 2); x * tile < area.Right; x += 2)
                context.FillRectangle(light, new Rect(x * tile, y * tile, tile, tile));
        }
    }

    private void RenderSelection(DrawingContext context, Annotation selected, double scale)
    {
        var bounds = AnnotationRenderer.ToRect(selected.Bounds.Inflate(selected.StrokeWidth / 2 + 4 / scale));
        if (selected is not ArrowAnnotation)
        {
            context.DrawRectangle(null, new Pen(Brushes.White, 3 / scale), bounds);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.DodgerBlue), 1.5 / scale,
                new DashStyle([4, 3], 0)), bounds);
        }
        var size = HandleSize / scale;
        var fill = Brushes.White;
        var border = new Pen(new SolidColorBrush(Colors.DodgerBlue), 1.5 / scale);
        foreach (var (_, position) in Handles(selected))
        {
            var rect = new Rect(position.X - size / 2, position.Y - size / 2, size, size);
            if (selected is BoxAnnotation) context.DrawRectangle(fill, border, rect, 1.5 / scale, 1.5 / scale);
            else context.DrawEllipse(fill, border, rect.Center, size / 2, size / 2);
        }
    }

    /// <summary>Grab points of an annotation: box edges/corners, arrow ends, counter tail, text width.</summary>
    private static IEnumerable<(string Id, PointD Position)> Handles(Annotation annotation)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                yield return ("start", arrow.Start);
                yield return ("end", arrow.End);
                break;
            case CounterAnnotation counter:
                // Dragging this pulls out (or moves) the pointer tail.
                yield return ("tail", counter.HasVisibleTail ? counter.Tail!.Value
                    : new PointD(counter.Center.X + counter.Radius * 1.6, counter.Center.Y + counter.Radius * 1.6));
                break;
            case TextAnnotation text:
                var b = text.Bounds;
                yield return ("w", new PointD(b.X, b.Y + b.Height / 2));
                yield return ("e", new PointD(b.Right, b.Y + b.Height / 2));
                break;
            case BoxAnnotation box:
                var r = box.Rect;
                double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                yield return ("nw", new PointD(r.X, r.Y));
                yield return ("n", new PointD(cx, r.Y));
                yield return ("ne", new PointD(r.Right, r.Y));
                yield return ("e", new PointD(r.Right, cy));
                yield return ("se", new PointD(r.Right, r.Bottom));
                yield return ("s", new PointD(cx, r.Bottom));
                yield return ("sw", new PointD(r.X, r.Bottom));
                yield return ("w", new PointD(r.X, cy));
                break;
        }
    }

    private string? HandleAt(PointD p)
    {
        if (Selected is null || Selected.Hidden || Selected == EditingText) return null;
        var reach = (HandleSize / 2 + 3) / DisplayScale;
        foreach (var (id, position) in Handles(Selected))
            if (Math.Abs(p.X - position.X) <= reach && Math.Abs(p.Y - position.Y) <= reach) return id;
        return null;
    }

    private static StandardCursorType HandleCursor(string id) => id switch
    {
        "n" or "s" => StandardCursorType.SizeNorthSouth,
        "e" or "w" => StandardCursorType.SizeWestEast,
        "nw" => StandardCursorType.TopLeftCorner,
        "ne" => StandardCursorType.TopRightCorner,
        "sw" => StandardCursorType.BottomLeftCorner,
        "se" => StandardCursorType.BottomRightCorner,
        _ => StandardCursorType.Hand,
    };

    /// <summary>Applies a handle drag, always relative to the state at drag start.</summary>
    private void DragHandle(Annotation target, Annotation original, string id, PointD p, bool shift)
    {
        switch (target, original)
        {
            case (ArrowAnnotation arrow, ArrowAnnotation _):
                if (id == "start") arrow.Start = shift ? SnapAngle(arrow.End, p) : p;
                else arrow.End = shift ? SnapAngle(arrow.Start, p) : p;
                break;
            case (CounterAnnotation counter, _):
                counter.Tail = shift ? SnapAngle(counter.Center, p) : p;
                break;
            case (TextAnnotation text, TextAnnotation start):
                var minWidth = text.FontSize * 1.5;
                var startRight = start.Bounds.Right;
                if (id == "w")
                {
                    var left = Math.Min(p.X, startRight - minWidth - 2 * text.Padding);
                    text.Position = text.Position with { X = left };
                    text.BoxWidth = startRight - left - 2 * text.Padding;
                }
                else
                {
                    text.BoxWidth = Math.Max(minWidth, p.X - text.Position.X - 2 * text.Padding);
                }
                break;
            case (BoxAnnotation box, BoxAnnotation start):
                var r = start.Rect;
                double left2 = r.X, top = r.Y, right = r.Right, bottom = r.Bottom;
                if (id.Contains('w')) left2 = p.X;
                if (id.Contains('e')) right = p.X;
                if (id.Contains('n')) top = p.Y;
                if (id.Contains('s')) bottom = p.Y;
                // Corner drags keep the aspect ratio for images (and for everything with Shift).
                if (id.Length == 2 && (box is ImageAnnotation) != shift && r.Width > 0 && r.Height > 0)
                {
                    var ratio = r.Width / r.Height;
                    var width = Math.Abs(right - left2);
                    var height = width / ratio;
                    if (id.Contains('n')) top = bottom - height;
                    else bottom = top + height;
                }
                box.Rect = RectD.FromPoints(new PointD(left2, top), new PointD(right, bottom));
                break;
        }
    }

    private void RenderTextRegions(DrawingContext context, double scale)
    {
        if (_textRegions is null) return;
        var outline = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x90, 0, 122, 255)), 1 / scale);
        var fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1C, 0, 122, 255));
        foreach (var word in _textRegions.SelectMany(l => l.Words))
            context.DrawRectangle(fill, outline, AnnotationRenderer.ToRect(word.Box), 2 / scale, 2 / scale);

        if (_hoverText is { } hover)
            context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x55, 0, 122, 255)),
                new Pen(new SolidColorBrush(Colors.DodgerBlue), 2 / scale), AnnotationRenderer.ToRect(PadText(hover)), 3 / scale, 3 / scale);
    }

    /// <summary>
    /// Margin around OCR boxes. OCR boxes hug the letters' x-height and tend to cut off
    /// descenders (g, p, y) and anti-aliased edges, so the bottom gets the most padding.
    /// </summary>
    internal static RectD PadText(RectD box)
    {
        var h = box.Height;
        var side = Math.Max(3, h * 0.2);
        var top = Math.Max(3, h * 0.3);
        var bottom = Math.Max(4, h * 0.5);
        return new RectD(box.X - side, box.Y - top, box.Width + 2 * side, box.Height + top + bottom);
    }

    /// <summary>Word under the point; with <paramref name="wholeLine"/> the entire line.</summary>
    private RectD? TextAt(PointD p, bool wholeLine)
    {
        if (_textRegions is null) return null;
        foreach (var line in _textRegions)
        {
            if (!PadText(line.Box).Contains(p)) continue;
            if (wholeLine) return line.Box;
            foreach (var word in line.Words)
                if (PadText(word.Box).Contains(p)) return word.Box;
        }
        return null;
    }

    private void RenderCropOverlay(DrawingContext context, Rect imageRect, double scale)
    {
        var crop = AnnotationRenderer.ToRect(CurrentCropRect);

        context.FillRectangle(CropDimBrush, new Rect(imageRect.X, imageRect.Y, imageRect.Width, crop.Top));
        context.FillRectangle(CropDimBrush, new Rect(imageRect.X, crop.Bottom, imageRect.Width, imageRect.Bottom - crop.Bottom));
        context.FillRectangle(CropDimBrush, new Rect(imageRect.X, crop.Top, crop.Left, crop.Height));
        context.FillRectangle(CropDimBrush, new Rect(crop.Right, crop.Top, imageRect.Right - crop.Right, crop.Height));

        // Rule-of-thirds guides, like in photo editors.
        var thin = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x70, 255, 255, 255)), 1 / scale);
        for (var i = 1; i < 3; i++)
        {
            var x = crop.X + crop.Width * i / 3;
            var y = crop.Y + crop.Height * i / 3;
            context.DrawLine(thin, new Point(x, crop.Top), new Point(x, crop.Bottom));
            context.DrawLine(thin, new Point(crop.Left, y), new Point(crop.Right, y));
        }
        context.DrawRectangle(null, new Pen(Brushes.White, 2 / scale), crop);

        // Corner brackets.
        var arm = Math.Min(24 / scale, Math.Min(crop.Width, crop.Height) / 3);
        var corners = new Pen(Brushes.White, 5 / scale, lineCap: PenLineCap.Square);
        foreach (var (cx, cy, dx, dy) in new[]
                 {
                     (crop.Left, crop.Top, 1, 1), (crop.Right, crop.Top, -1, 1),
                     (crop.Left, crop.Bottom, 1, -1), (crop.Right, crop.Bottom, -1, -1),
                 })
        {
            context.DrawLine(corners, new Point(cx, cy), new Point(cx + dx * arm, cy));
            context.DrawLine(corners, new Point(cx, cy), new Point(cx, cy + dy * arm));
        }

        // Edge handles show that the frame itself can be dragged.
        var handle = new Pen(Brushes.White, 5 / scale, lineCap: PenLineCap.Round);
        var half = Math.Min(14 / scale, Math.Min(crop.Width, crop.Height) / 4);
        context.DrawLine(handle, new Point(crop.Center.X - half, crop.Top), new Point(crop.Center.X + half, crop.Top));
        context.DrawLine(handle, new Point(crop.Center.X - half, crop.Bottom), new Point(crop.Center.X + half, crop.Bottom));
        context.DrawLine(handle, new Point(crop.Left, crop.Center.Y - half), new Point(crop.Left, crop.Center.Y + half));
        context.DrawLine(handle, new Point(crop.Right, crop.Center.Y - half), new Point(crop.Right, crop.Center.Y + half));

        var label = new FormattedText(
            string.Create(CultureInfo.InvariantCulture, $"{Math.Round(crop.Width)} × {Math.Round(crop.Height)}"),
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 12 / scale, Brushes.White);
        var labelRect = new Rect(crop.X + 6 / scale, crop.Y + 6 / scale, label.Width + 12 / scale, label.Height + 6 / scale);
        context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(0xC0, 20, 20, 20)), null, labelRect, 4 / scale, 4 / scale);
        context.DrawText(label, new Point(labelRect.X + 6 / scale, labelRect.Y + 3 / scale));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();

        var p = ToImage(point.Position);
        _dragStart = _lastPoint = p;
        _dragStartScreen = _lastScreen = ScreenPosition(e);
        _lastView = ViewRect;
        var hitTolerance = 6 / DisplayScale;
        var scaling = _capture.Scaling;

        // Handles of the selected annotation work with every tool.
        if (Tool is not (EditorTool.Crop or EditorTool.ColorPicker) && HandleAt(p) is { } handle)
        {
            _activeHandle = handle;
            _handleOriginal = Selected!.Clone();
            _handleRecorded = false;
            e.Pointer.Capture(this);
            return;
        }

        // Double-click edits text, whatever tool is active.
        if (e.ClickCount == 2 && Tool != EditorTool.Crop && _capture.Annotations.HitTest(p, hitTolerance) is TextAnnotation clickedText)
        {
            Select(clickedText);
            TextEditRequested?.Invoke(clickedText.Position, clickedText);
            return;
        }

        switch (Tool)
        {
            case EditorTool.Select:
                var hit = _capture.Annotations.HitTest(p, hitTolerance);
                Select(hit);
                if (hit is TextAnnotation text && e.ClickCount == 2)
                {
                    TextEditRequested?.Invoke(text.Position, text);
                    return;
                }
                if (hit is not null)
                {
                    AnnotationClicked?.Invoke();
                    _moving = true;
                    _moveRecorded = false;
                    e.Pointer.Capture(this);
                }
                return;

            case EditorTool.Crop:
                // Grab an edge/corner of the current frame, move it, or draw a new one outside.
                var current = _capture.VisibleRect;
                _cropStart = new RectD(current.X, current.Y, current.Width, current.Height);
                _cropDrag = HitCropFrame(p);
                _cropDraft = _cropDrag == CropDrag.New ? new RectD(ClampX(p.X), ClampY(p.Y), 0, 0) : _cropStart;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;

            case EditorTool.Text:
                if (_capture.Annotations.HitTest(p, hitTolerance) is TextAnnotation existing)
                    TextEditRequested?.Invoke(existing.Position, existing);
                else
                    TextEditRequested?.Invoke(p, null);
                return;

            case EditorTool.Counter:
                // Placed on release; dragging pulls out a pointer tail.
                Select(null);
                EditingStarted?.Invoke();
                _draft = new CounterAnnotation
                {
                    Center = p,
                    Number = _capture.Annotations.NextCounterNumber(),
                    Color = Color,
                    Radius = ToolSizes.CounterRadiusFor(StrokePoints) * scaling,
                };
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;

            case EditorTool.ColorPicker:
                ColorPicked?.Invoke(_capture.Image.GetPixel((int)p.X, (int)p.Y));
                return;

            case EditorTool.TextRecognition:
                _ocrDraft = new RectD(p.X, p.Y, 0, 0);
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;

            default:
                // Pixelate: remember a clicked text so a click without dragging blurs it.
                _pressedText = Tool == EditorTool.Pixelate ? TextAt(p, e.KeyModifiers.HasFlag(KeyModifiers.Alt)) : null;
                Select(null);
                EditingStarted?.Invoke();
                _draft = CreateDraft(p);
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = ToImage(e.GetPosition(this));
        PointerImagePositionChanged?.Invoke(p);
        var screen = ScreenPosition(e);

        if (_activeHandle is { } activeHandle && Selected is { } target && _handleOriginal is { } original)
        {
            if (!_handleRecorded)
            {
                _capture.Annotations.BeginChange();
                _handleRecorded = true;
                EditingStarted?.Invoke();
                BeginGrowingDrag();
            }
            // Measured from the press in screen space: stays correct while the canvas grows and shifts.
            var anchored = new PointD(_dragStart.X + (screen.X - _dragStartScreen.X) / DisplayScale,
                _dragStart.Y + (screen.Y - _dragStartScreen.Y) / DisplayScale);
            DragHandle(target, original, activeHandle, anchored, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _capture.Annotations.NotifyChanged();
            ReportViewShift();
        }
        else if (_ocrDraft is not null)
        {
            _ocrDraft = RectD.FromPoints(_dragStart, p);
            InvalidateVisual();
        }
        else if (_cropDraft is not null)
        {
            _cropDraft = DragCrop(p);
            InvalidateVisual();
        }
        else if (Tool == EditorTool.Crop)
        {
            Cursor = new Cursor(HitCropFrame(p) switch
            {
                CropDrag.Left or CropDrag.Right => StandardCursorType.SizeWestEast,
                CropDrag.Top or CropDrag.Bottom => StandardCursorType.SizeNorthSouth,
                CropDrag.Left | CropDrag.Top => StandardCursorType.TopLeftCorner,
                CropDrag.Right | CropDrag.Top => StandardCursorType.TopRightCorner,
                CropDrag.Left | CropDrag.Bottom => StandardCursorType.BottomLeftCorner,
                CropDrag.Right | CropDrag.Bottom => StandardCursorType.BottomRightCorner,
                CropDrag.Move => StandardCursorType.SizeAll,
                _ => StandardCursorType.Cross,
            });
        }
        else if (_draft is not null)
        {
            UpdateDraft(_draft, p, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            InvalidateVisual();
        }
        else if (Tool != EditorTool.Crop && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false && HandleAt(p) is { } hoverHandle)
        {
            Cursor = new Cursor(HandleCursor(hoverHandle));
        }
        else if (Tool == EditorTool.Pixelate)
        {
            Cursor = _toolCursor;
            var hover = TextAt(p, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            if (hover != _hoverText)
            {
                _hoverText = hover;
                InvalidateVisual();
            }
        }
        else if (_moving && Selected is not null)
        {
            if (!_moveRecorded)
            {
                _capture.Annotations.BeginChange();
                _moveRecorded = true;
                EditingStarted?.Invoke();
                BeginGrowingDrag();
            }
            Selected.Move((screen.X - _lastScreen.X) / DisplayScale, (screen.Y - _lastScreen.Y) / DisplayScale);
            _capture.Annotations.NotifyChanged();
            ReportViewShift();
        }
        else if (_draft is null && !_moving)
        {
            Cursor = _toolCursor;
        }
        _lastPoint = p;
        _lastScreen = screen;
    }

    /// <summary>Pointer position relative to the window, which does not move when the canvas grows.</summary>
    private Point ScreenPosition(PointerEventArgs e) =>
        TopLevel.GetTopLevel(this) is { } top ? e.GetPosition(top) : e.GetPosition(this);

    private void BeginGrowingDrag()
    {
        _dragGrowing = true;
        _lastView = ViewRect;
        DragStarted?.Invoke();
    }

    private void ReportViewShift()
    {
        var view = ViewRect;
        if (view.X != _lastView.X || view.Y != _lastView.Y)
            ViewOriginShifted?.Invoke(view.X - _lastView.X, view.Y - _lastView.Y);
        _lastView = view;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _moving = false;
        if (_activeHandle is not null)
        {
            _activeHandle = null;
            _handleOriginal = null;
            InvalidateMeasure();
        }
        if (_dragGrowing)
        {
            _dragGrowing = false;
            DragEnded?.Invoke();
        }

        if (_ocrDraft is { } ocr)
        {
            _ocrDraft = null;
            InvalidateVisual();
            var region = IntRect.FromRectD(ocr).Intersect(new IntRect(0, 0, _capture.Image.Width, _capture.Image.Height));
            if (region.Width >= MinCropSize && region.Height >= MinCropSize) TextRegionSelected?.Invoke(region);
            return;
        }

        if (_cropDraft is { } crop)
        {
            _cropDraft = null;
            if (crop.Width >= MinCropSize && crop.Height >= MinCropSize)
            {
                var full = _capture.ContentBounds;
                var rect = new IntRect((int)Math.Round(crop.X), (int)Math.Round(crop.Y),
                    (int)Math.Round(crop.Width), (int)Math.Round(crop.Height)).Intersect(full);
                _capture.Annotations.SetCrop(rect == full ? null : rect);
            }
            InvalidateVisual();
            return;
        }

        if (_draft is null) return;
        var draft = _draft;
        _draft = null;
        var b = draft.Bounds;
        if (draft is PixelateAnnotation pixelate && b.Width < MinShapeSize && b.Height < MinShapeSize && _pressedText is { } text)
        {
            pixelate.Rect = PadText(text);
            _capture.Annotations.Add(pixelate);
            Select(pixelate);
            return;
        }
        if (draft is CounterAnnotation || b.Width >= MinShapeSize || b.Height >= MinShapeSize)
        {
            _capture.Annotations.Add(draft);
            // The new annotation stays selected: its options and handles can be used right away.
            Select(draft);
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerImagePositionChanged?.Invoke(null);
    }

    private Annotation CreateDraft(PointD p)
    {
        var scaling = _capture.Scaling;
        var stroke = StrokePoints * scaling;
        var rect = new RectD(p.X, p.Y, 0, 0);
        return Tool switch
        {
            EditorTool.Arrow => new ArrowAnnotation { Start = p, End = p, Color = Color, StrokeWidth = stroke },
            EditorTool.Rectangle => new RectangleAnnotation { Rect = rect, Color = Color, StrokeWidth = stroke },
            EditorTool.Ellipse => new EllipseAnnotation { Rect = rect, Color = Color, StrokeWidth = stroke },
            EditorTool.Highlight => new HighlightAnnotation { Rect = rect, Color = Color, StrokeWidth = 0 },
            EditorTool.Pixelate => new PixelateAnnotation
            {
                Rect = rect,
                Style = ObscureStyle,
                BlockSize = (int)Math.Round(ToolSizes.BlockSizeFor(ObscureStrength) * scaling),
                BlurRadius = (int)Math.Round(ToolSizes.BlurRadiusFor(ObscureStrength) * scaling),
                StrokeWidth = 0,
            },
            EditorTool.Spotlight => new SpotlightAnnotation { Rect = rect, StrokeWidth = 0 },
            _ => throw new InvalidOperationException($"{Tool} does not draw shapes"),
        };
    }

    private void UpdateDraft(Annotation draft, PointD p, bool constrain)
    {
        switch (draft)
        {
            case ArrowAnnotation arrow:
                arrow.End = constrain ? SnapAngle(arrow.Start, p) : p;
                break;
            case CounterAnnotation counter:
                counter.Tail = constrain ? SnapAngle(counter.Center, p) : p;
                break;
            case BoxAnnotation box:
                if (constrain)
                {
                    // Shift: square / circle.
                    var size = Math.Max(Math.Abs(p.X - _dragStart.X), Math.Abs(p.Y - _dragStart.Y));
                    p = new PointD(_dragStart.X + Math.Sign(p.X - _dragStart.X) * size,
                        _dragStart.Y + Math.Sign(p.Y - _dragStart.Y) * size);
                }
                box.Rect = RectD.FromPoints(_dragStart, p);
                break;
        }
    }

    /// <summary>Shift-drag snaps to 45° steps.</summary>
    private static PointD SnapAngle(PointD start, PointD p)
    {
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new PointD(start.X + Math.Cos(angle) * length, start.Y + Math.Sin(angle) * length);
    }

    [Flags]
    private enum CropDrag
    {
        New = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Move = 16,
    }

    private RectD CurrentCropRect =>
        _cropDraft ?? (_capture.VisibleRect is var v ? new RectD(v.X, v.Y, v.Width, v.Height) : default);

    /// <summary>Which part of the crop frame is under the point (edges get a generous grab zone).</summary>
    private CropDrag HitCropFrame(PointD p)
    {
        var frame = CurrentCropRect;
        var grab = 10 / DisplayScale;
        if (!frame.Inflate(grab).Contains(p)) return CropDrag.New;
        var drag = CropDrag.New;
        if (Math.Abs(p.X - frame.X) <= grab) drag |= CropDrag.Left;
        else if (Math.Abs(p.X - frame.Right) <= grab) drag |= CropDrag.Right;
        if (Math.Abs(p.Y - frame.Y) <= grab) drag |= CropDrag.Top;
        else if (Math.Abs(p.Y - frame.Bottom) <= grab) drag |= CropDrag.Bottom;
        if (drag != CropDrag.New) return drag;
        // Moving an uncropped frame is pointless; dragging inside it starts a new one.
        var content = _capture.ContentBounds;
        var isFullImage = frame.Width >= content.Width && frame.Height >= content.Height;
        return isFullImage ? CropDrag.New : CropDrag.Move;
    }

    private RectD DragCrop(PointD p)
    {
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        var s = _cropStart;
        var c = _capture.ContentBounds;

        if (_cropDrag == CropDrag.New)
            return RectD.FromPoints(_dragStart, new PointD(ClampX(p.X), ClampY(p.Y)));
        if (_cropDrag == CropDrag.Move)
            return s with { X = Math.Clamp(s.X + dx, c.X, c.Right - s.Width), Y = Math.Clamp(s.Y + dy, c.Y, c.Bottom - s.Height) };

        double left = s.X, top = s.Y, right = s.Right, bottom = s.Bottom;
        if (_cropDrag.HasFlag(CropDrag.Left)) left = Math.Clamp(s.X + dx, c.X, right - MinCropSize);
        if (_cropDrag.HasFlag(CropDrag.Right)) right = Math.Clamp(s.Right + dx, left + MinCropSize, c.Right);
        if (_cropDrag.HasFlag(CropDrag.Top)) top = Math.Clamp(s.Y + dy, c.Y, bottom - MinCropSize);
        if (_cropDrag.HasFlag(CropDrag.Bottom)) bottom = Math.Clamp(s.Bottom + dy, top + MinCropSize, c.Bottom);
        return new RectD(left, top, right - left, bottom - top);
    }

    private double ClampX(double x) => Math.Clamp(x, _capture.ContentBounds.X, _capture.ContentBounds.Right);

    private double ClampY(double y) => Math.Clamp(y, _capture.ContentBounds.Y, _capture.ContentBounds.Bottom);

    private PointD ToImage(Point p)
    {
        var view = ViewRect;
        return new PointD(p.X / DisplayScale + view.X, p.Y / DisplayScale + view.Y);
    }
}
