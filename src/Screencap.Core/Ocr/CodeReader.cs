using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using ZXing;
using ZXing.Common;

namespace Screencap.Core.Ocr;

/// <summary>A decoded QR code or barcode; the box is in image pixels.</summary>
public sealed record DecodedCode(string Text, string Format, RectD Box)
{
    /// <summary>True for web links, so the UI can offer "open link".</summary>
    public bool IsLink => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out var uri) &&
                          (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// Reads QR codes and common barcodes with ZXing.Net — pure managed code, so it behaves
/// identically on macOS and Windows.
/// </summary>
public static class CodeReader
{
    private static readonly IList<BarcodeFormat> Formats =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A, BarcodeFormat.UPC_E, BarcodeFormat.ITF,
    ];

    public static IReadOnlyList<DecodedCode> Read(PixelBuffer image)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = Formats },
        };
        var source = new RGBLuminanceSource(image.Data, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source) ?? [];
        return results
            .Where(r => !string.IsNullOrEmpty(r.Text))
            .GroupBy(r => r.Text)
            .Select(g => g.First())
            .Select(r => new DecodedCode(r.Text, FormatName(r.BarcodeFormat), BoxOf(r.ResultPoints)))
            .ToList();
    }

    private static string FormatName(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => "QR-Code",
        BarcodeFormat.DATA_MATRIX => "Data Matrix",
        BarcodeFormat.AZTEC => "Aztec",
        BarcodeFormat.PDF_417 => "PDF417",
        _ => "Barcode",
    };

    private static RectD BoxOf(ResultPoint[]? points)
    {
        if (points is not { Length: > 0 }) return default;
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        return new RectD(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
    }
}
