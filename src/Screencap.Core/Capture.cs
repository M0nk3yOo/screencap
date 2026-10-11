using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.Core;

/// <summary>One screenshot and everything drawn on it.</summary>
public sealed class Capture(PixelBuffer image, double scaling)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public PixelBuffer Image { get; } = image;

    /// <summary>
    /// Display scaling of the screen it came from (2.0 on Retina). Needed to show the
    /// image at its original size: one image pixel = one screen pixel.
    /// </summary>
    public double Scaling { get; } = scaling;

    public AnnotationDocument Annotations { get; init; } = new();

    /// <summary>
    /// The whole drawing area: the screenshot plus every visible annotation, so moving an
    /// image, text or shape past the edge grows the canvas instead of cutting it off.
    /// Pixelate and spotlight only act on the screenshot itself and never grow it.
    /// </summary>
    public IntRect ContentBounds
    {
        get
        {
            var bounds = new IntRect(0, 0, Image.Width, Image.Height);
            foreach (var annotation in Annotations.Items)
            {
                if (annotation.Hidden || annotation is PixelateAnnotation or SpotlightAnnotation) continue;
                var b = annotation.Bounds;
                if (b.Width <= 0 && b.Height <= 0) continue;
                bounds = bounds.Union(IntRect.FromRectD(b));
            }
            return bounds;
        }
    }

    /// <summary>Application whose window was captured (best guess), e.g. "Safari".</summary>
    public string? SourceApp { get; set; }

    /// <summary>Title of the captured window (best guess).</summary>
    public string? SourceTitle { get; set; }

    /// <summary>"Safari – Startseite", for display.</summary>
    public string? SourceDescription =>
        (SourceApp, SourceTitle) switch
        {
            (null or "", null or "") => null,
            (null or "", var title) => title,
            (var app, null or "") => app,
            var (app, title) => $"{app} – {title}",
        };

    /// <summary>Where this screenshot was last saved, for "show in Finder/Explorer".</summary>
    public string? LastSavedPath { get; set; }

    /// <summary>The part that is shown and exported: the crop, or the whole content.</summary>
    public IntRect VisibleRect =>
        Annotations.Crop is { } crop ? crop.Intersect(ContentBounds) : ContentBounds;

    public string SuggestedFileName => ScreenshotFiles.FileName(CreatedAt);

    public string FileNameFor(Settings.ImageFormat format) =>
        ScreenshotFiles.FileName(CreatedAt, format == Settings.ImageFormat.Jpeg ? ".jpg" : ".png");
}
