using System.Buffers.Binary;
using System.Globalization;
using System.Security;
using System.Text;

namespace Screencap.Core.Imaging;

/// <summary>Where a screenshot came from; written into saved PNG/JPEG files.</summary>
public sealed record ImageMetadata(string? App, string? WindowTitle, DateTimeOffset CapturedAt)
{
    public static string Software { get; } =
        "Screencap " + (typeof(ImageMetadata).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "");

    /// <summary>Window title, or the app name if there is no title.</summary>
    public string? Title => string.IsNullOrWhiteSpace(WindowTitle) ? App : WindowTitle;

    /// <summary>"Safari – Startseite".</summary>
    public string Description =>
        string.Join(" – ", new[] { App, WindowTitle }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Adds metadata to encoded images without re-encoding them: PNG text chunks plus XMP, and
/// for JPEG an EXIF block (incl. the Windows XPTitle/XPComment tags) plus XMP.
/// </summary>
public static class MetadataWriter
{
    private const int MaxTextLength = 500;

    // ------------------------------------------------------------------ PNG

    public static byte[] AddToPng(byte[] png, ImageMetadata metadata)
    {
        const int signatureLength = 8;
        // The first chunk is always IHDR (13 data bytes + length, type and CRC).
        var insertAt = signatureLength + 4 + 4 + 13 + 4;
        if (png.Length < insertAt || Encoding.ASCII.GetString(png, signatureLength + 4, 4) != "IHDR")
            throw new InvalidDataException("Not a PNG file");

        using var chunks = new MemoryStream();
        WritePngChunk(chunks, "tEXt", Latin1("Software"), Latin1(ImageMetadata.Software));
        WritePngChunk(chunks, "tEXt", Latin1("Creation Time"), Latin1(metadata.CapturedAt.ToString("R", CultureInfo.InvariantCulture)));
        if (Limit(metadata.Title) is { } title) WriteInternationalText(chunks, "Title", title);
        if (Limit(metadata.Description) is { Length: > 0 } description) WriteInternationalText(chunks, "Description", description);
        if (Limit(metadata.App) is { } app) WriteInternationalText(chunks, "Screencap:Application", app);
        if (Limit(metadata.WindowTitle) is { } window) WriteInternationalText(chunks, "Screencap:WindowTitle", window);
        WriteInternationalText(chunks, "XML:com.adobe.xmp", Xmp(metadata));

        var result = new byte[png.Length + chunks.Length];
        Buffer.BlockCopy(png, 0, result, 0, insertAt);
        chunks.ToArray().CopyTo(result, insertAt);
        Buffer.BlockCopy(png, insertAt, result, insertAt + (int)chunks.Length, png.Length - insertAt);
        return result;
    }

    /// <summary>iTXt: keyword, no compression, no language tag, UTF-8 text.</summary>
    private static void WriteInternationalText(Stream output, string keyword, string text)
    {
        var data = new List<byte>();
        data.AddRange(Latin1(keyword));
        data.AddRange([0, 0, 0, 0, 0]); // null, compression flag, method, empty language tag, empty translated keyword
        data.AddRange(Encoding.UTF8.GetBytes(text));
        WritePngChunk(output, "iTXt", data.ToArray());
    }

    private static void WritePngChunk(Stream output, string type, byte[] keyword, byte[] text)
    {
        var data = new byte[keyword.Length + 1 + text.Length];
        keyword.CopyTo(data, 0);
        text.CopyTo(data, keyword.Length + 1);
        WritePngChunk(output, type, data);
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32.Compute(typeBytes, data));
        output.Write(buffer);
    }

    // ------------------------------------------------------------------ JPEG

    public static byte[] AddToJpeg(byte[] jpeg, ImageMetadata metadata)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new InvalidDataException("Not a JPEG file");

        // After SOI and a JFIF APP0 segment, if present — the order Apple's ImageIO writes too.
        var insertAt = 2;
        if (jpeg[2] == 0xFF && jpeg[3] == 0xE0) insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4));

        using var segments = new MemoryStream();
        WriteJpegSegment(segments, 0xE1, Exif(metadata));
        WriteJpegSegment(segments, 0xE1, [.. Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"), .. Encoding.UTF8.GetBytes(Xmp(metadata))]);

        var result = new byte[jpeg.Length + segments.Length];
        Buffer.BlockCopy(jpeg, 0, result, 0, insertAt);
        segments.ToArray().CopyTo(result, insertAt);
        Buffer.BlockCopy(jpeg, insertAt, result, insertAt + (int)segments.Length, jpeg.Length - insertAt);
        return result;
    }

    private static void WriteJpegSegment(Stream output, byte marker, byte[] payload)
    {
        if (payload.Length + 2 > ushort.MaxValue) return; // too large for one segment; skip rather than corrupt
        output.WriteByte(0xFF);
        output.WriteByte(marker);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        output.Write(length);
        output.Write(payload);
    }

    /// <summary>Minimal little-endian EXIF block with one IFD.</summary>
    private static byte[] Exif(ImageMetadata metadata)
    {
        // (tag, type, value bytes) — must be sorted by tag.
        var entries = new List<(ushort Tag, ushort Type, byte[] Value)>();
        if (Limit(metadata.Description) is { Length: > 0 } description)
            entries.Add((0x010E, 2, [.. Encoding.UTF8.GetBytes(description), 0])); // ImageDescription
        entries.Add((0x0131, 2, [.. Encoding.ASCII.GetBytes(ImageMetadata.Software), 0])); // Software
        entries.Add((0x0132, 2, [.. Encoding.ASCII.GetBytes(metadata.CapturedAt.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)), 0])); // DateTime
        if (Limit(metadata.Title) is { } title) entries.Add((0x9C9B, 1, Ucs2(title))); // XPTitle (Windows Explorer)
        if (Limit(metadata.Description) is { Length: > 0 } comment) entries.Add((0x9C9C, 1, Ucs2(comment))); // XPComment

        const int tiffHeader = 8;
        var ifdSize = 2 + entries.Count * 12 + 4;
        var dataOffset = tiffHeader + ifdSize;
        var tiff = new MemoryStream();
        var w = new BinaryWriter(tiff);
        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)42);
        w.Write(8u); // IFD0 right after the header
        w.Write((ushort)entries.Count);
        var data = new MemoryStream();
        foreach (var (tag, type, value) in entries)
        {
            w.Write(tag);
            w.Write(type);
            w.Write((uint)value.Length);
            if (value.Length <= 4)
            {
                var inline = new byte[4];
                value.CopyTo(inline, 0);
                w.Write(inline);
            }
            else
            {
                w.Write((uint)(dataOffset + data.Length));
                data.Write(value);
                if (data.Length % 2 == 1) data.WriteByte(0); // keep offsets word-aligned
            }
        }
        w.Write(0u); // no next IFD
        w.Write(data.ToArray());
        return [.. Encoding.ASCII.GetBytes("Exif\0\0"), .. tiff.ToArray()];
    }

    // ------------------------------------------------------------------ shared

    private static string Xmp(ImageMetadata m)
    {
        static string Esc(string s) => SecurityElement.Escape(s);
        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.Append("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        sb.Append(" xmlns:screencap=\"https://github.com/M0nk3yOo/screencap/ns/1.0/\">");
        sb.Append($"<xmp:CreatorTool>{Esc(ImageMetadata.Software)}</xmp:CreatorTool>");
        sb.Append($"<xmp:CreateDate>{m.CapturedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)}</xmp:CreateDate>");
        if (Limit(m.Title) is { } title)
            sb.Append($"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{Esc(title)}</rdf:li></rdf:Alt></dc:title>");
        if (Limit(m.Description) is { Length: > 0 } description)
            sb.Append($"<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{Esc(description)}</rdf:li></rdf:Alt></dc:description>");
        if (Limit(m.App) is { } app) sb.Append($"<screencap:Application>{Esc(app)}</screencap:Application>");
        if (Limit(m.WindowTitle) is { } window) sb.Append($"<screencap:WindowTitle>{Esc(window)}</screencap:WindowTitle>");
        sb.Append("</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");
        return sb.ToString();
    }

    private static string? Limit(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length > MaxTextLength ? s[..MaxTextLength] : s;

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Ucs2(string s) => [.. Encoding.Unicode.GetBytes(s), 0, 0];

    private static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in type) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
