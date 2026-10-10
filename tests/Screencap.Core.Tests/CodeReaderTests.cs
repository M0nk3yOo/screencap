using Screencap.Core.Imaging;
using Screencap.Core.Ocr;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Screencap.Core.Tests;

public class CodeReaderTests
{
    internal static PixelBuffer RenderQr(string text, int size = 240, int canvas = 400)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2 },
        };
        var qr = writer.Write(text);
        // Place the code on a larger white "screenshot".
        var data = Enumerable.Repeat((byte)255, canvas * canvas * 4).ToArray();
        for (var y = 0; y < qr.Height; y++)
            Buffer.BlockCopy(qr.Pixels, y * qr.Width * 4, data, ((y + 60) * canvas + 80) * 4, qr.Width * 4);
        return new PixelBuffer(canvas, canvas, data);
    }

    [Fact]
    public void ReadsQrCodeInScreenshot()
    {
        var code = Assert.Single(CodeReader.Read(RenderQr("https://screencap.example/hallo")));
        Assert.Equal("https://screencap.example/hallo", code.Text);
        Assert.Equal("QR-Code", code.Format);
        Assert.True(code.IsLink);
        Assert.InRange(code.Box.X, 70, 140);
    }

    [Fact]
    public void PlainTextIsNotALink()
    {
        var code = Assert.Single(CodeReader.Read(RenderQr("WIFI:S:Gast;T:WPA;P:geheim;;")));
        Assert.False(code.IsLink);
    }

    [Fact]
    public void NoCode_ReturnsEmpty() => Assert.Empty(CodeReader.Read(PixelBufferTests.Gradient(200, 100)));
}
