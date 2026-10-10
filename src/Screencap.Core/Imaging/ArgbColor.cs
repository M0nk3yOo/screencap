using System.Globalization;

namespace Screencap.Core.Imaging;

public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public uint Value => (uint)(A << 24 | R << 16 | G << 8 | B);

    public static ArgbColor FromUInt(uint v) => new((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    public ArgbColor WithAlpha(byte a) => this with { A = a };

    /// <summary>"#RRGGBB" — what users expect when picking a color from the screen.</summary>
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>Relative luminance in 0..1, used to pick readable label colors.</summary>
    public double Luminance => (0.2126 * R + 0.7152 * G + 0.0722 * B) / 255.0;
}
