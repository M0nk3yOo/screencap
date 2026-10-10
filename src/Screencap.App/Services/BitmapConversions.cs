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

    public static unsafe PixelBuffer ToPixelBuffer(this Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var data = new byte[size.Width * size.Height * 4];
        fixed (byte* p = data)
            bitmap.CopyPixels(new PixelRect(size), (IntPtr)p, data.Length, size.Width * 4);
        return new PixelBuffer(size.Width, size.Height, data);
    }

    public static NativeWindowHandle? GetNativeHandle(this TopLevel topLevel) =>
        topLevel.TryGetPlatformHandle() is { } h ? new NativeWindowHandle(h.Handle, h.HandleDescriptor ?? "") : null;
}
