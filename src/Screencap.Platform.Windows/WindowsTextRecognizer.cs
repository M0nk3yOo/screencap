using System.Runtime.InteropServices.WindowsRuntime;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Screencap.Platform.Windows;

/// <summary>
/// OCR via Windows.Media.Ocr, built into Windows 10/11. Uses the languages of the user
/// profile; a language needs its OCR component installed (Settings → Language).
/// </summary>
public sealed class WindowsTextRecognizer : ITextRecognizer
{
    public async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(PixelBuffer image, CancellationToken cancellationToken = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Für keine der Windows-Sprachen ist die Texterkennung installiert (Einstellungen → Zeit und Sprache → Sprache).");

        // The engine rejects images above MaxImageDimension; shrink and scale the boxes back.
        var factor = (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)OcrEngine.MaxImageDimension);
        var input = factor > 1 ? image.Downscale(factor) : image;

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(input.Data.AsBuffer(), BitmapPixelFormat.Bgra8,
            input.Width, input.Height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);

        var scale = Math.Max(1, factor);
        return result.Lines.Select(line =>
        {
            var words = line.Words
                .Select(w => new RecognizedWord(w.Text, new RectD(
                    w.BoundingRect.X * scale, w.BoundingRect.Y * scale, w.BoundingRect.Width * scale, w.BoundingRect.Height * scale)))
                .ToList();
            var x = words.Min(w => w.Box.X);
            var y = words.Min(w => w.Box.Y);
            var right = words.Max(w => w.Box.Right);
            var bottom = words.Max(w => w.Box.Bottom);
            return new RecognizedLine(line.Text, new RectD(x, y, right - x, bottom - y), words);
        }).ToList();
    }
}
