using System.Text;
using Screencap.Core.Imaging;
using Screencap.Core.Geometry;
using Screencap.Core.Platform;

namespace Screencap.Core.Tests;

public class MetadataTests
{
    private static readonly ImageMetadata Sample =
        new("Safari", "Grüße – Übersicht <Test>", new DateTimeOffset(2026, 10, 10, 14, 33, 5, TimeSpan.FromHours(2)));

    [Fact]
    public void Png_StaysValid_AndContainsTextChunks()
    {
        var png = PixelBufferTests.Gradient(20, 10).EncodePng();
        var tagged = MetadataWriter.AddToPng(png, Sample);

        var decoded = PixelBuffer.Decode(tagged);
        Assert.Equal(20, decoded.Width);

        var chunks = ReadPngChunks(tagged);
        Assert.Contains(chunks, c => c.Type == "iTXt" && c.Text.StartsWith("Title") && c.Text.Contains("Grüße – Übersicht <Test>"));
        Assert.Contains(chunks, c => c.Type == "iTXt" && c.Text.StartsWith("Screencap:Application") && c.Text.EndsWith("Safari"));
        Assert.Contains(chunks, c => c.Type == "iTXt" && c.Text.Contains("&lt;Test&gt;")); // XMP is escaped
        Assert.Contains(chunks, c => c.Type == "tEXt" && c.Text.StartsWith("Software\0Screencap"));
        Assert.Equal("IEND", chunks[^1].Type);
    }

    [Fact]
    public void Jpeg_StaysValid_AndContainsExifAndXmp()
    {
        var jpeg = PixelBufferTests.Gradient(20, 10).EncodeJpeg(90, new ArgbColor(255, 255, 255, 255));
        var tagged = MetadataWriter.AddToJpeg(jpeg, Sample);

        Assert.Equal(20, PixelBuffer.Decode(tagged).Width);
        var raw = Encoding.Latin1.GetString(tagged);
        Assert.Contains("Exif\0\0II*\0", raw);
        Assert.Contains("http://ns.adobe.com/xap/1.0/", raw);
        Assert.Contains(Encoding.Latin1.GetString(Encoding.Unicode.GetBytes("Grüße")), raw); // XPTitle, UCS-2
        Assert.Contains("2026:10:10 14:33:05", raw);
    }

    [Fact]
    public void WithoutWindowInfo_StillWritesSoftwareAndDate()
    {
        var tagged = MetadataWriter.AddToPng(PixelBufferTests.Gradient(4, 4).EncodePng(), Sample with { App = null, WindowTitle = null });
        var chunks = ReadPngChunks(tagged);
        Assert.DoesNotContain(chunks, c => c.Text.StartsWith("Title"));
        Assert.Contains(chunks, c => c.Text.StartsWith("Creation Time"));
    }

    [Fact]
    public void WindowMatcher_PicksFrontmostWindowUnderRegionCenter()
    {
        WindowInfo[] windows =
        [
            new("Tooltip", "", new IntRect(500, 300, 40, 20)),
            new("Finder", "Downloads", new IntRect(0, 0, 400, 300)),
            new("Safari", "Startseite", new IntRect(0, 0, 1200, 800)),
        ];
        Assert.Equal("Finder", WindowMatcher.Find(windows, new IntRect(100, 100, 50, 50))?.App);
        Assert.Equal("Safari", WindowMatcher.Find(windows, new IntRect(480, 280, 80, 60))?.App);
        Assert.Null(WindowMatcher.Find(windows, new IntRect(2000, 2000, 10, 10)));
    }

    private sealed record Chunk(string Type, string Text);

    private static List<Chunk> ReadPngChunks(byte[] png)
    {
        var chunks = new List<Chunk>();
        for (var i = 8; i < png.Length;)
        {
            var length = (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
            var type = Encoding.ASCII.GetString(png, i + 4, 4);
            var text = type is "iTXt" ? Encoding.UTF8.GetString(png, i + 8, length) : Encoding.Latin1.GetString(png, i + 8, length);
            chunks.Add(new Chunk(type, text));
            i += 12 + length;
        }
        return chunks;
    }
}
