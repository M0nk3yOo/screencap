using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;
using Screencap.Core.Settings;

namespace Screencap.App.Tests;

internal sealed class FakePlatform : IPlatformServices, IScreenCapturer, IGlobalHotkeys, IWindowPlatform, ITextRecognizer, IImageClipboard, IWindowInspector, IAutostart
{
    public IScreenCapturer Capturer => this;
    public IGlobalHotkeys Hotkeys => this;
    public IWindowPlatform Windows => this;
    public ITextRecognizer TextRecognizer => this;
    public IImageClipboard Clipboard => this;
    public IWindowInspector WindowInspector => this;
    public IAutostart Autostart => this;

    public IReadOnlyList<WindowInfo> Windows_ { get; set; } = [];
    public IReadOnlyList<WindowInfo> GetWindows() => Windows_;

    public bool IsSupported { get; set; } = true;
    public bool IsEnabled { get; private set; }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    public PixelBuffer? ClipboardImage { get; private set; }
    public bool DockIconVisible { get; private set; }

    public void SetImage(PixelBuffer image) => ClipboardImage = image;
    public void SetDockIconVisible(bool visible) => DockIconVisible = visible;

    public IReadOnlyList<RecognizedLine> OcrResult { get; set; } = [];

    public Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(PixelBuffer image, CancellationToken cancellationToken = default) =>
        Task.FromResult(OcrResult);

    public bool HasPermission() => true;
    public void RequestPermission() { }
    /// <summary>Optional scripted captures (e.g. a page being scrolled); default is a solid image.</summary>
    public Func<ScreenInfo, PixelBuffer>? CaptureProvider { get; set; }

    public Task<PixelBuffer> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default) =>
        Task.FromResult(CaptureProvider?.Invoke(screen) ?? TestImages.Solid(screen.Bounds.Width, screen.Bounds.Height));

    public bool TryRegister(Hotkey hotkey, Action onPressed) => true;
    public void UnregisterAll() { }
    public void Dispose() { }

    public void ConfigureOverlay(NativeWindowHandle window, IntRect screenBounds) { }
    public void ConfigurePinned(NativeWindowHandle window) { }
    public void ExcludeFromCapture(NativeWindowHandle window) { }
    public void ActivateApplication() { }
    public PointD GetCursorPosition() => default;
}

internal static class TestImages
{
    public static PixelBuffer Solid(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = 40;
            data[i + 1] = 120;
            data[i + 2] = 200;
            data[i + 3] = 255;
        }
        return new PixelBuffer(width, height, data);
    }
}
