using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

public sealed class MacPlatformServices : IPlatformServices
{
    public IScreenCapturer Capturer { get; } = new MacScreenCapturer();
    public IGlobalHotkeys Hotkeys { get; } = new MacGlobalHotkeys();
    public IWindowPlatform Windows { get; } = new MacWindowPlatform();
    public ITextRecognizer TextRecognizer { get; } = new MacTextRecognizer();
    public IImageClipboard Clipboard { get; } = new MacImageClipboard();
}
