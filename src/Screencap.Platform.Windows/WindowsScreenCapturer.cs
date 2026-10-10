using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

/// <summary>
/// GDI capture. Simple, no permission needed and no yellow capture border.
/// Known limits: HDR screens look washed out and DRM-protected windows come out black;
/// Windows.Graphics.Capture would address HDR later.
/// </summary>
public sealed unsafe class WindowsScreenCapturer : IScreenCapturer
{
    public bool HasPermission() => true;

    public void RequestPermission() { }

    public Task<PixelBuffer> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default) =>
        // Avalonia makes the process per-monitor DPI aware, so its screen bounds are physical
        // pixels in virtual-screen coordinates — the same space the screen DC uses.
        Task.FromResult(Capture(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height));

    private static PixelBuffer Capture(int x, int y, int width, int height)
    {
        var screenDc = Win32.GetDC(IntPtr.Zero);
        var memoryDc = Win32.CreateCompatibleDC(screenDc);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Win32.BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // negative = top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = Win32.CreateDIBSection(screenDc, &header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            var previous = Win32.SelectObject(memoryDc, bitmap);
            var ok = Win32.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT);
            Win32.SelectObject(memoryDc, previous);
            if (!ok) throw new InvalidOperationException("BitBlt failed");

            var data = new byte[width * height * 4];
            new ReadOnlySpan<byte>((void*)bits, data.Length).CopyTo(data);
            // GDI leaves the alpha channel undefined; screenshots are opaque.
            for (var i = 3; i < data.Length; i += 4) data[i] = 255;
            return new PixelBuffer(width, height, data);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) Win32.DeleteObject(bitmap);
            Win32.DeleteDC(memoryDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
