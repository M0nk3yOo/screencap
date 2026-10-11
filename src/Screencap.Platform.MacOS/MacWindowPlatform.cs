using Screencap.Core.Geometry;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

public sealed class MacWindowPlatform : IWindowPlatform
{
    // NSWindowLevel values (CGWindowLevel.h)
    private const nint ScreenSaverWindowLevel = 1000;

    // NSWindowCollectionBehavior flags
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint FullScreenAuxiliary = 1 << 8;

    public void ConfigureOverlay(NativeWindowHandle window, IntRect screenBounds)
    {
        var nsWindow = ToNSWindow(window);
        // Above the menu bar and the Dock, also on top of full-screen apps.
        ObjC.SendVoidLong(nsWindow, ObjC.sel_registerName("setLevel:"), ScreenSaverWindowLevel);
        ObjC.SendVoidULong(nsWindow, ObjC.sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | FullScreenAuxiliary);

        // AppKit/Avalonia constrain the window to the visible frame (without menu bar and
        // Dock). Setting the frame directly after raising the level gets the full screen.
        // Cocoa frames use a bottom-left origin on the main display.
        var mainHeight = CoreGraphics.CGDisplayBounds(CoreGraphics.CGMainDisplayID()).Height;
        var frame = new CGRect
        {
            X = screenBounds.X,
            Y = mainHeight - screenBounds.Bottom,
            Width = screenBounds.Width,
            Height = screenBounds.Height,
        };
        ObjC.SendVoidRectBool(nsWindow, ObjC.sel_registerName("setFrame:display:"), frame, true);
    }

    // NSWindowSharingNone: the window is left out of screenshots and screen recordings.
    public void ExcludeFromCapture(NativeWindowHandle window) =>
        ObjC.SendVoidULong(ToNSWindow(window), ObjC.sel_registerName("setSharingType:"), 0);

    public void ConfigurePinned(NativeWindowHandle window)
    {
        var nsWindow = ToNSWindow(window);
        ObjC.SendVoidULong(nsWindow, ObjC.sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | FullScreenAuxiliary);
        // A pin must not disappear when another app becomes active.
        ObjC.SendVoidBool(nsWindow, ObjC.sel_registerName("setHidesOnDeactivate:"), false);
    }

    public void ActivateApplication() =>
        ObjC.SendVoidBool(ObjC.SharedApplication, ObjC.sel_registerName("activateIgnoringOtherApps:"), true);

    public void SetDockIconVisible(bool visible)
    {
        // NSApplicationActivationPolicyRegular = 0, Accessory = 1 (menu bar app without Dock icon).
        ObjC.SendBoolLong(ObjC.SharedApplication, ObjC.sel_registerName("setActivationPolicy:"), visible ? 0 : 1);
        if (visible) ActivateApplication();
    }

    public PointD GetCursorPosition()
    {
        // NSEvent reports points with a bottom-left origin on the main display;
        // Avalonia uses points with a top-left origin.
        var p = ObjC.SendPoint(ObjC.objc_getClass("NSEvent"), ObjC.sel_registerName("mouseLocation"));
        var main = CoreGraphics.CGDisplayBounds(CoreGraphics.CGMainDisplayID());
        return new PointD(p.X, main.Height - p.Y);
    }

    private static IntPtr ToNSWindow(NativeWindowHandle window) => window.Descriptor switch
    {
        "NSWindow" => window.Handle,
        "NSView" => ObjC.Send(window.Handle, "window"),
        _ => throw new ArgumentException($"Unexpected handle type {window.Descriptor}", nameof(window)),
    };
}
