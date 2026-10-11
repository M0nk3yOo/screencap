using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Settings;

namespace Screencap.Core.Platform;

/// <summary>
/// A screen as reported by the UI toolkit: bounds in physical pixels, in the toolkit's
/// global coordinate space, plus the display scaling factor.
/// </summary>
public sealed record ScreenInfo(IntRect Bounds, double Scaling);

/// <summary>Raw native window handle, as handed out by the UI toolkit.</summary>
public readonly record struct NativeWindowHandle(IntPtr Handle, string Descriptor);

/// <summary>Everything that has to be implemented natively per operating system.</summary>
public interface IPlatformServices
{
    IScreenCapturer Capturer { get; }
    IGlobalHotkeys Hotkeys { get; }
    IWindowPlatform Windows { get; }
    ITextRecognizer TextRecognizer { get; }
    IImageClipboard Clipboard { get; }
    IWindowInspector WindowInspector { get; }
    IAutostart Autostart { get; }
}

/// <summary>A visible application window; bounds in the toolkit's screen coordinates.</summary>
public sealed record WindowInfo(string App, string Title, IntRect Bounds);

public interface IWindowInspector
{
    /// <summary>Visible windows of other apps, frontmost first. Empty if the OS doesn't allow it.</summary>
    IReadOnlyList<WindowInfo> GetWindows();
}

/// <summary>Start Screencap when the user logs in.</summary>
public interface IAutostart
{
    /// <summary>False when running from a development build that can't be registered.</summary>
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>
/// Puts images on the system clipboard in the formats other apps actually read
/// (PNG + TIFF on macOS, DIB + PNG on Windows).
/// </summary>
public interface IImageClipboard
{
    void SetImage(PixelBuffer image);
}

/// <summary>A line of text found by OCR; boxes are in image pixels (top-left origin).</summary>
public sealed record RecognizedLine(string Text, RectD Box, IReadOnlyList<RecognizedWord> Words);

public sealed record RecognizedWord(string Text, RectD Box);

public interface ITextRecognizer
{
    /// <summary>Recognizes text with the operating system's built-in OCR. May run on a background thread.</summary>
    Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(PixelBuffer image, CancellationToken cancellationToken = default);
}

public interface IScreenCapturer
{
    /// <summary>macOS requires the "Screen Recording" permission; Windows always returns true.</summary>
    bool HasPermission();

    /// <summary>Shows the system prompt (or settings pane) for the permission.</summary>
    void RequestPermission();

    Task<PixelBuffer> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}

public interface IGlobalHotkeys : IDisposable
{
    /// <summary>Returns false if the combination is taken by another app or the system.</summary>
    bool TryRegister(Hotkey hotkey, Action onPressed);

    void UnregisterAll();
}

public interface IWindowPlatform
{
    /// <summary>
    /// Lifts the selection overlay above menu bar / taskbar and makes it cover exactly
    /// <paramref name="screenBounds"/> (toolkit coordinates, see <see cref="ScreenInfo"/>).
    /// Needed because macOS shrinks borderless windows to the area between menu bar and Dock.
    /// </summary>
    void ConfigureOverlay(NativeWindowHandle window, IntRect screenBounds);

    /// <summary>Hides one of our own windows from screen captures (scroll capture controls).</summary>
    void ExcludeFromCapture(NativeWindowHandle window);

    /// <summary>Keeps a pinned screenshot visible on all spaces / virtual desktops.</summary>
    void ConfigurePinned(NativeWindowHandle window);

    /// <summary>Brings the app to the foreground so its windows receive keyboard input.</summary>
    void ActivateApplication();

    /// <summary>
    /// Shows or hides the Dock icon (macOS). The app normally lives in the menu bar only,
    /// but while the main window is open it should behave like a regular app.
    /// </summary>
    void SetDockIconVisible(bool visible);

    /// <summary>Mouse position in the same coordinate space as <see cref="ScreenInfo.Bounds"/>.</summary>
    PointD GetCursorPosition();
}
