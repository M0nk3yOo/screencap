using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.Core.Tests;

public class PixelBufferTests
{
    internal static PixelBuffer Gradient(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            data[i] = (byte)x; // B
            data[i + 1] = (byte)y; // G
            data[i + 2] = 200; // R
            data[i + 3] = 255;
        }
        return new PixelBuffer(width, height, data);
    }

    [Fact]
    public void Crop_CopiesTheRightPixels()
    {
        var crop = Gradient(10, 10).Crop(new IntRect(2, 3, 4, 5));
        Assert.Equal(4, crop.Width);
        Assert.Equal(5, crop.Height);
        Assert.Equal(new ArgbColor(255, 200, 3, 2), crop.GetPixel(0, 0));
        Assert.Equal(new ArgbColor(255, 200, 7, 5), crop.GetPixel(3, 4));
    }

    [Fact]
    public void Crop_ClipsToImage()
    {
        var crop = Gradient(10, 10).Crop(new IntRect(8, 8, 5, 5));
        Assert.Equal(2, crop.Width);
        Assert.Equal(2, crop.Height);
    }

    [Fact]
    public void Average_OfBlock()
    {
        var avg = Gradient(4, 4).Average(new IntRect(0, 0, 4, 2));
        Assert.Equal(new ArgbColor(255, 200, 0, 1), avg); // G: (0+1)/2 = 0, B: (0+1+2+3)/4 = 1
    }

    [Fact]
    public void Png_RoundTripIsLossless()
    {
        var original = Gradient(17, 9);
        var decoded = PixelBuffer.Decode(original.EncodePng());
        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Data, decoded.Data);
    }

    [Fact]
    public void Hex_IsUppercaseRgb()
    {
        Assert.Equal("#FF3B30", ArgbColor.FromUInt(0xFFFF3B30).ToHex());
    }
}
