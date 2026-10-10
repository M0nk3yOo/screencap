using Screencap.Core.Geometry;
using SkiaSharp;

namespace Screencap.Core.Imaging;

/// <summary>
/// Raw screenshot pixels: BGRA8888, premultiplied, tightly packed (stride = Width * 4).
/// This layout maps 1:1 onto Avalonia's WriteableBitmap and onto GDI/CoreGraphics buffers.
/// </summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public int Stride => Width * 4;

    public PixelBuffer(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (data.Length < width * height * 4) throw new ArgumentException("Buffer too small", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    public static PixelBuffer Decode(ReadOnlySpan<byte> encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported image format");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* p = pixels)
            {
                var result = codec.GetPixels(info, (IntPtr)p);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    throw new InvalidDataException($"Image decode failed: {result}");
            }
        }
        return new PixelBuffer(info.Width, info.Height, pixels);
    }

    public byte[] EncodePng()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        unsafe
        {
            fixed (byte* p = Data)
            {
                using var pixmap = new SKPixmap(info, (IntPtr)p, Stride);
                using var encoded = pixmap.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("PNG encode failed");
                return encoded.ToArray();
            }
        }
    }

    /// <summary>JPEG has no alpha: transparent areas are composited onto <paramref name="background"/>.</summary>
    public byte[] EncodeJpeg(int quality, ArgbColor background)
    {
        var opaque = CompositeOnto(background);
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        unsafe
        {
            fixed (byte* p = opaque.Data)
            {
                using var pixmap = new SKPixmap(info, (IntPtr)p, Stride);
                using var encoded = pixmap.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100))
                    ?? throw new InvalidOperationException("JPEG encode failed");
                return encoded.ToArray();
            }
        }
    }

    /// <summary>Copy with every pixel made opaque by blending it over <paramref name="background"/>.</summary>
    public PixelBuffer CompositeOnto(ArgbColor background)
    {
        var data = (byte[])Data.Clone();
        for (var i = 0; i < data.Length; i += 4)
        {
            var alpha = data[i + 3];
            if (alpha == 255) continue;
            // Premultiplied: result = source + background * (1 - alpha).
            var inverse = 255 - alpha;
            data[i] = (byte)(data[i] + background.B * inverse / 255);
            data[i + 1] = (byte)(data[i + 1] + background.G * inverse / 255);
            data[i + 2] = (byte)(data[i + 2] + background.R * inverse / 255);
            data[i + 3] = 255;
        }
        return new PixelBuffer(Width, Height, data);
    }

    public PixelBuffer Crop(IntRect rect)
    {
        var r = rect.Intersect(new IntRect(0, 0, Width, Height));
        if (r.IsEmpty) throw new ArgumentException("Crop rectangle outside image", nameof(rect));
        var result = new byte[r.Width * r.Height * 4];
        for (var y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Data, ((r.Y + y) * Width + r.X) * 4, result, y * r.Width * 4, r.Width * 4);
        return new PixelBuffer(r.Width, r.Height, result);
    }

    /// <summary>Shrinks by an integer factor, averaging each block (good enough for OCR input).</summary>
    public PixelBuffer Downscale(int factor)
    {
        if (factor <= 1) return this;
        var width = Math.Max(1, Width / factor);
        var height = Math.Max(1, Height / factor);
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var c = Average(new IntRect(x * factor, y * factor, factor, factor));
            var i = (y * width + x) * 4;
            // Average() returns straight alpha; screenshots are opaque, so this stays premultiplied-correct.
            result[i] = c.B;
            result[i + 1] = c.G;
            result[i + 2] = c.R;
            result[i + 3] = c.A;
        }
        return new PixelBuffer(width, height, result);
    }

    /// <summary>
    /// Blurred copy of <paramref name="region"/>. Three box-blur passes approximate a Gaussian.
    /// Pixels around the region are sampled too, so the blur has no hard dark edges.
    /// </summary>
    public PixelBuffer Blur(IntRect region, int radius)
    {
        region = region.Intersect(new IntRect(0, 0, Width, Height));
        if (region.IsEmpty) throw new ArgumentException("Region outside image", nameof(region));
        radius = Math.Max(1, radius);

        var margin = radius * 3;
        var source = new IntRect(region.X - margin, region.Y - margin, region.Width + 2 * margin, region.Height + 2 * margin)
            .Intersect(new IntRect(0, 0, Width, Height));
        var work = Crop(source);
        var buffer = new byte[work.Data.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(work.Data, buffer, work.Width, work.Height, radius);
            BoxBlurHorizontal(Transpose(buffer, work.Width, work.Height), work.Data, work.Height, work.Width, radius);
            Array.Copy(Transpose(work.Data, work.Height, work.Width), work.Data, work.Data.Length);
        }
        return work.Crop(new IntRect(region.X - source.X, region.Y - source.Y, region.Width, region.Height));
    }

    /// <summary>Sliding-window box blur along rows, edges clamped. O(width) per row regardless of radius.</summary>
    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int width, int height, int radius)
    {
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var i = -radius; i <= radius; i++)
                    sum += src[row + Math.Clamp(i, 0, width - 1) * 4 + c];
                for (var x = 0; x < width; x++)
                {
                    dst[row + x * 4 + c] = (byte)(sum / window);
                    var add = Math.Min(x + radius + 1, width - 1);
                    var remove = Math.Max(x - radius, 0);
                    sum += src[row + add * 4 + c] - src[row + remove * 4 + c];
                }
            }
        }
    }

    private static byte[] Transpose(byte[] src, int width, int height)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            Buffer.BlockCopy(src, (y * width + x) * 4, dst, (x * height + y) * 4, 4);
        return dst;
    }

    public ArgbColor GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var i = (y * Width + x) * 4;
        return Unpremultiply(Data[i + 3], Data[i + 2], Data[i + 1], Data[i]);
    }

    /// <summary>Average color of a rectangle; the basis of the pixelate tool.</summary>
    public ArgbColor Average(IntRect rect)
    {
        var r = rect.Intersect(new IntRect(0, 0, Width, Height));
        if (r.IsEmpty) return default;
        long a = 0, red = 0, g = 0, b = 0;
        for (var y = r.Y; y < r.Bottom; y++)
        {
            var i = (y * Width + r.X) * 4;
            for (var x = 0; x < r.Width; x++, i += 4)
            {
                b += Data[i];
                g += Data[i + 1];
                red += Data[i + 2];
                a += Data[i + 3];
            }
        }
        var n = (long)r.Width * r.Height;
        return Unpremultiply((byte)(a / n), (byte)(red / n), (byte)(g / n), (byte)(b / n));
    }

    private static ArgbColor Unpremultiply(byte a, byte r, byte g, byte b)
    {
        if (a is 0) return default;
        if (a is 255) return new ArgbColor(a, r, g, b);
        return new ArgbColor(a, (byte)(r * 255 / a), (byte)(g * 255 / a), (byte)(b * 255 / a));
    }
}
