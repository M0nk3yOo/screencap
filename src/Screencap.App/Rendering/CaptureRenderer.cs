using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.App.Rendering;

internal static class CaptureRenderer
{
    /// <summary>
    /// Visible (cropped) part of the screenshot with all annotations burned in, at full resolution.
    /// <paramref name="background"/> fills canvas area outside the screenshot; alpha 0 keeps it transparent.
    /// </summary>
    public static Bitmap Flatten(Capture capture, ArgbColor background, Bitmap? baseImage = null)
    {
        var image = capture.Image;
        var visible = capture.VisibleRect;
        var target = new RenderTargetBitmap(new PixelSize(visible.Width, visible.Height), new Vector(96, 96));
        using (var dc = target.CreateDrawingContext())
        using (dc.PushTransform(Matrix.CreateTranslation(-visible.X, -visible.Y)))
        {
            var content = capture.ContentBounds;
            if (background.A > 0 && content != new IntRect(0, 0, image.Width, image.Height))
                dc.FillRectangle(new SolidColorBrush(AnnotationRenderer.ToColor(background)),
                    new Rect(content.X, content.Y, content.Width, content.Height));
            var source = baseImage ?? image.ToBitmap();
            dc.DrawImage(source, new Rect(0, 0, image.Width, image.Height));
            AnnotationRenderer.RenderAll(dc, capture.Annotations.Items, image);
            if (baseImage is null) source.Dispose();
        }
        return target;
    }
}
