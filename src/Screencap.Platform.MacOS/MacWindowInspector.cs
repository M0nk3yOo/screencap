using System.Runtime.InteropServices;
using Screencap.Core.Geometry;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

/// <summary>
/// Lists on-screen windows via CGWindowListCopyWindowInfo. Window titles are only provided
/// with the Screen Recording permission, which Screencap needs anyway.
/// </summary>
public sealed unsafe partial class MacWindowInspector : IWindowInspector
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const uint OnScreenOnly = 1 << 0;
    private const uint ExcludeDesktopElements = 1 << 4;
    private const uint Utf8 = 0x08000100;
    private const int SInt32Type = 3;

    private static readonly IntPtr KeyOwnerName = Key("kCGWindowOwnerName");
    private static readonly IntPtr KeyName = Key("kCGWindowName");
    private static readonly IntPtr KeyLayer = Key("kCGWindowLayer");
    private static readonly IntPtr KeyBounds = Key("kCGWindowBounds");
    private static readonly IntPtr KeyOwnerPid = Key("kCGWindowOwnerPID");

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var result = new List<WindowInfo>();
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return result;
        try
        {
            var count = CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                var window = CFArrayGetValueAtIndex(list, i);
                // Layer 0 = normal app windows; menus, Dock and overlays live on other layers.
                if (Int(window, KeyLayer) != 0 || Int(window, KeyOwnerPid) == Environment.ProcessId) continue;
                var boundsDict = CFDictionaryGetValue(window, KeyBounds);
                if (boundsDict == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(boundsDict, out var b)) continue;

                // CoreGraphics uses points with a top-left origin — the same space Avalonia reports on macOS.
                result.Add(new WindowInfo(
                    Text(window, KeyOwnerName) ?? "",
                    Text(window, KeyName) ?? "",
                    new IntRect((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height)));
            }
        }
        finally
        {
            CFRelease(list);
        }
        return result;
    }

    private static int? Int(IntPtr dict, IntPtr key)
    {
        var number = CFDictionaryGetValue(dict, key);
        if (number == IntPtr.Zero) return null;
        int value;
        return CFNumberGetValue(number, SInt32Type, &value) ? value : null;
    }

    private static string? Text(IntPtr dict, IntPtr key)
    {
        var str = CFDictionaryGetValue(dict, key);
        if (str == IntPtr.Zero) return null;
        var length = CFStringGetLength(str);
        var size = length * 4 + 1;
        var buffer = stackalloc byte[(int)Math.Min(size, 4096)];
        return CFStringGetCString(str, buffer, Math.Min(size, 4096), Utf8) ? Marshal.PtrToStringUTF8((IntPtr)buffer) : null;
    }

    /// <summary>The kCGWindow* constants are CFStrings whose value equals their name.</summary>
    private static IntPtr Key(string name) => CFStringCreateWithCString(IntPtr.Zero, name, Utf8);

    [LibraryImport(CoreGraphicsLib)]
    private static partial IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [LibraryImport(CoreGraphicsLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, int* value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(IntPtr str);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(IntPtr str, byte* buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr obj);
}
