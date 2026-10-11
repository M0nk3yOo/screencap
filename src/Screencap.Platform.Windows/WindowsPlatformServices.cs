using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

public sealed class WindowsPlatformServices : IPlatformServices
{
    public IScreenCapturer Capturer { get; } = new WindowsScreenCapturer();
    public IGlobalHotkeys Hotkeys { get; } = new WindowsGlobalHotkeys();
    public IWindowPlatform Windows { get; } = new WindowsWindowPlatform();
    public ITextRecognizer TextRecognizer { get; } = new WindowsTextRecognizer();
    public IImageClipboard Clipboard { get; } = new WindowsImageClipboard();
    public IWindowInspector WindowInspector { get; } = new WindowsWindowInspector();
    public IAutostart Autostart { get; } = new WindowsAutostart();
}
