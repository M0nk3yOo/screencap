using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.App.Services;

internal static class BitmapConversions
{
    public static WriteableBitmap ToBitmap(this PixelBuffer buffer)
    {
        // 96 DPI: one bitmap pixel = one unit when drawn, scaling is applied explicitly.
        var bitmap = new WriteableBitmap(new PixelSize(buffer.Width, buffer.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = bitmap.Lock();
        for (var y = 0; y < buffer.Height; y++)
            Marshal.Copy(buffer.Data, y * buffer.Stride, locked.Address + y * locked.RowBytes, buffer.Stride);
        return bitmap;
    }

    /// <summary>
    /// Pixels of a rendered bitmap as BGRA. Goes through PNG on purpose: CopyPixels returns
    /// the backend's native layout (RGBA with Skia on macOS), which swapped red and blue.
    /// </summary>
    public static PixelBuffer ToPixelBuffer(this Bitmap bitmap) => PixelBuffer.Decode(bitmap.ToPng());

    public static byte[] ToPng(this Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    public static NativeWindowHandle? GetNativeHandle(this TopLevel topLevel) =>
        topLevel.TryGetPlatformHandle() is { } h ? new NativeWindowHandle(h.Handle, h.HandleDescriptor ?? "") : null;
}
