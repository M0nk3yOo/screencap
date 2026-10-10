using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

/// <summary>
/// Writes images as CF_DIB (understood by every Windows app, Outlook included) and as
/// "PNG" (keeps transparency for apps that support it, e.g. Office, browsers).
/// </summary>
public sealed unsafe class WindowsImageClipboard : IImageClipboard
{
    public void SetImage(PixelBuffer image)
    {
        // DIB readers ignore alpha (transparent would turn black), so it gets a white background.
        var dib = CreateDib(image.CompositeOnto(new ArgbColor(255, 255, 255, 255)));
        var png = CreateGlobal(image.EncodePng());
        try
        {
            OpenWithRetry();
            try
            {
                Win32.EmptyClipboard();
                // On success the clipboard owns the memory; only free it on failure.
                if (Win32.SetClipboardData(Win32.CF_DIB, dib) != IntPtr.Zero) dib = IntPtr.Zero;
                if (Win32.SetClipboardData(Win32.RegisterClipboardFormatW("PNG"), png) != IntPtr.Zero) png = IntPtr.Zero;
            }
            finally
            {
                Win32.CloseClipboard();
            }
        }
        finally
        {
            if (dib != IntPtr.Zero) Win32.GlobalFree(dib);
            if (png != IntPtr.Zero) Win32.GlobalFree(png);
        }
    }

    /// <summary>Another app may hold the clipboard for a moment.</summary>
    private static void OpenWithRetry()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Win32.OpenClipboard(IntPtr.Zero)) return;
            Thread.Sleep(20);
        }
        throw new InvalidOperationException("Die Zwischenablage wird gerade von einer anderen Anwendung verwendet.");
    }

    private static IntPtr CreateDib(PixelBuffer image)
    {
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Win32.BITMAPINFOHEADER),
            biWidth = image.Width,
            biHeight = image.Height, // positive = bottom-up rows, what most readers expect
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)(image.Stride * image.Height),
        };
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)(sizeof(Win32.BITMAPINFOHEADER) + image.Stride * image.Height));
        var target = (byte*)Win32.GlobalLock(memory);
        try
        {
            *(Win32.BITMAPINFOHEADER*)target = header;
            var pixels = target + sizeof(Win32.BITMAPINFOHEADER);
            fixed (byte* source = image.Data)
            {
                for (var y = 0; y < image.Height; y++)
                    Buffer.MemoryCopy(source + y * image.Stride, pixels + (image.Height - 1 - y) * image.Stride, image.Stride, image.Stride);
            }
        }
        finally
        {
            Win32.GlobalUnlock(memory);
        }
        return memory;
    }

    private static IntPtr CreateGlobal(byte[] bytes)
    {
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)bytes.Length);
        var target = Win32.GlobalLock(memory);
        try
        {
            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, (void*)target, bytes.Length, bytes.Length);
        }
        finally
        {
            Win32.GlobalUnlock(memory);
        }
        return memory;
    }
}
