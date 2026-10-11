namespace Screencap.Core.Imaging;

/// <summary>
/// Builds one tall image from frames of a region captured while the user scrolls down.
/// Each new frame is aligned to the previous one by finding the vertical shift where their
/// rows match best; only the newly revealed rows are appended.
/// </summary>
public sealed class ScrollStitcher
{
    /// <summary>Safety limit for the stitched height (pixels).</summary>
    public const int MaxHeight = 40000;

    private readonly List<byte[]> _rows = [];
    private PixelBuffer? _last;

    public int Width { get; private set; }

    public int Height => _rows.Count;

    public int FrameCount { get; private set; }

    /// <summary>Adds a frame; returns the number of new rows (0 = no scrolling detected).</summary>
    public int Add(PixelBuffer frame)
    {
        if (_last is null)
        {
            Width = frame.Width;
            for (var y = 0; y < frame.Height; y++) _rows.Add(Row(frame, y));
            _last = frame;
            FrameCount = 1;
            return frame.Height;
        }
        if (frame.Width != Width || frame.Height != _last.Height) throw new ArgumentException("Frames must have the same size", nameof(frame));
        if (Height >= MaxHeight) return 0;

        var shift = FindShift(_last, frame);
        if (shift <= 0) return 0;

        var added = Math.Min(shift, MaxHeight - Height);
        for (var y = frame.Height - shift; y < frame.Height - shift + added; y++) _rows.Add(Row(frame, y));
        _last = frame;
        FrameCount++;
        return added;
    }

    public PixelBuffer Result()
    {
        if (_rows.Count == 0) throw new InvalidOperationException("No frames");
        var stride = Width * 4;
        var data = new byte[stride * _rows.Count];
        for (var y = 0; y < _rows.Count; y++) Buffer.BlockCopy(_rows[y], 0, data, y * stride, stride);
        return new PixelBuffer(Width, _rows.Count, data);
    }

    /// <summary>
    /// How many pixels <paramref name="next"/> is scrolled down compared to <paramref name="previous"/>:
    /// the shift s where previous rows [s..h) best match next rows [0..h−s). Returns 0 if
    /// nothing moved or no reliable match exists.
    /// </summary>
    public static int FindShift(PixelBuffer previous, PixelBuffer next)
    {
        var h = previous.Height;
        var a = RowSignatures(previous);
        var b = RowSignatures(next);

        // Unchanged frame?
        if (Difference(a, b, 0, h) < 0.5) return 0;

        // Require at least a quarter of the frame to overlap so a match is meaningful.
        var minOverlap = Math.Max(8, h / 4);
        var bestShift = 0;
        var bestScore = double.MaxValue;
        for (var s = 1; s <= h - minOverlap; s++)
        {
            var score = Difference(a, b, s, h - s);
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = s;
            }
        }
        return bestScore < 6 ? bestShift : 0;
    }

    /// <summary>Mean absolute difference between a[shift..shift+count) and b[0..count).</summary>
    private static double Difference(ulong[][] a, ulong[][] b, int shift, int count)
    {
        double total = 0;
        var columns = a[0].Length;
        for (var y = 0; y < count; y++)
        {
            var ra = a[y + shift];
            var rb = b[y];
            for (var c = 0; c < columns; c++) total += Math.Abs((long)ra[c] - (long)rb[c]);
        }
        return total / (count * columns);
    }

    /// <summary>
    /// Per row: brightness sums over a few column bands. Cheap to compare and robust against
    /// subpixel noise, but still distinguishes rows with text in different places.
    /// </summary>
    private static ulong[][] RowSignatures(PixelBuffer image)
    {
        const int bands = 16;
        var result = new ulong[image.Height][];
        var bandWidth = Math.Max(1, image.Width / bands);
        for (var y = 0; y < image.Height; y++)
        {
            var row = new ulong[bands];
            var offset = y * image.Stride;
            for (var x = 0; x < image.Width; x++)
            {
                var i = offset + x * 4;
                row[Math.Min(bands - 1, x / bandWidth)] += (ulong)(image.Data[i] + image.Data[i + 1] + image.Data[i + 2]);
            }
            for (var c = 0; c < bands; c++) row[c] /= (ulong)bandWidth;
            result[y] = row;
        }
        return result;
    }

    private static byte[] Row(PixelBuffer image, int y)
    {
        var row = new byte[image.Stride];
        Buffer.BlockCopy(image.Data, y * image.Stride, row, 0, image.Stride);
        return row;
    }
}
