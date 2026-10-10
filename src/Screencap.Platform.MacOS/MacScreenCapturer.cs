using System.Diagnostics;
using System.Globalization;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

/// <summary>
/// Captures via the system's <c>screencapture</c> tool. It is stable across macOS versions,
/// whereas CGDisplayCreateImage is deprecated. ScreenCaptureKit would be faster and is the
/// planned replacement once the bridging code is worth it.
/// </summary>
public sealed class MacScreenCapturer : IScreenCapturer
{
    public bool HasPermission() => CoreGraphics.CGPreflightScreenCaptureAccess();

    public void RequestPermission()
    {
        // Shows the system prompt only once per app; afterwards open the settings pane directly.
        if (!CoreGraphics.CGRequestScreenCaptureAccess())
            Process.Start("open", "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture");
    }

    public async Task<PixelBuffer> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        // Avalonia reports macOS screen bounds in points with a top-left origin, which is
        // exactly what -R expects. The resulting image has full Retina resolution.
        var b = screen.Bounds;
        var file = Path.Combine(Path.GetTempPath(), $"screencap-{Guid.NewGuid():N}.png");
        var region = string.Create(CultureInfo.InvariantCulture, $"{b.X},{b.Y},{b.Width},{b.Height}");
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/screencapture")
            {
                ArgumentList = { "-x", "-t", "png", "-R", region, file },
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("screencapture could not be started");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(file))
                throw new InvalidOperationException(
                    $"screencapture failed ({process.ExitCode}): {await process.StandardError.ReadToEndAsync(cancellationToken)}");
            return PixelBuffer.Decode(await File.ReadAllBytesAsync(file, cancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
