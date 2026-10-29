using Screencap.Core.Geometry;
using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

public sealed class WindowsWindowPlatform : IWindowPlatform
{
    // Bounds are physical pixels (per-monitor DPI aware), as SetWindowPos expects.
    public void ConfigureOverlay(NativeWindowHandle window, IntRect screenBounds)
    {
        Win32.SetWindowPos(window.Handle, Win32.HWND_TOPMOST, screenBounds.X, screenBounds.Y,
            screenBounds.Width, screenBounds.Height, Win32.SWP_NOACTIVATE);
        // Without the foreground the overlay gets no key presses, so Esc could not cancel.
        Win32.SetForegroundWindow(window.Handle);
    }

    // WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+): BitBlt and other capture APIs skip the window.
    public void ExcludeFromCapture(NativeWindowHandle window) => Win32.SetWindowDisplayAffinity(window.Handle, 0x11);

    // Showing a window on all virtual desktops has no public API (only the undocumented
    // IVirtualDesktopManagerInternal COM interface), so pins stay on their desktop for now.
    public void ConfigurePinned(NativeWindowHandle window) { }

    // A hotkey press counts as user input, so Windows lets the app take the foreground.
    public void ActivateApplication() { }

    // Windows shows the main window in the taskbar on its own.
    public void SetDockIconVisible(bool visible) { }

    public PointD GetCursorPosition() =>
        Win32.GetCursorPos(out var p) ? new PointD(p.X, p.Y) : default;
}
