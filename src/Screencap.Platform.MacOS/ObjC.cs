using System.Runtime.InteropServices;

namespace Screencap.Platform.MacOS;

/// <summary>Minimal Objective-C runtime bridge; enough to call a handful of AppKit methods.</summary>
internal static partial class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_registerName(string name);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidLong(IntPtr receiver, IntPtr selector, nint arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidULong(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial void SendVoidRectBool(IntPtr receiver, IntPtr selector, CGRect rect, [MarshalAs(UnmanagedType.U1)] bool arg);

    /// <summary>For methods returning NSPoint/CGPoint (two doubles, returned in registers on arm64 and x64).</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtrPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtrPtrPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtrULong(IntPtr receiver, IntPtr selector, nuint arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtrBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBoolPtrPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBoolLong(IntPtr receiver, IntPtr selector, nint arg);

    /// <summary>Autoreleased NSString from a .NET string.</summary>
    public static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return SendPtrPtr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>Autoreleased NSData copy of the bytes.</summary>
    public static unsafe IntPtr NSData(byte[] bytes)
    {
        fixed (byte* p = bytes)
            return SendPtrBytes(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), (IntPtr)p, (nuint)bytes.Length);
    }

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial nuint SendULong(IntPtr receiver, IntPtr selector);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendPtrRangeOut(IntPtr receiver, IntPtr selector, NSRange range, out IntPtr error);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBoolPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBoolPtrOut(IntPtr receiver, IntPtr selector, IntPtr arg, out IntPtr error);

    /// <summary>CGRect-returning methods on arm64 (returned in registers).</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend")]
    private static partial CGRect SendRectArm64(IntPtr receiver, IntPtr selector);

    /// <summary>CGRect-returning methods on x86_64 (large structs come back via hidden pointer).</summary>
    [LibraryImport(Lib, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectStret(out CGRect result, IntPtr receiver, IntPtr selector);

    public static CGRect SendRect(IntPtr receiver, string selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return SendRectArm64(receiver, sel_registerName(selector));
        SendRectStret(out var rect, receiver, sel_registerName(selector));
        return rect;
    }

    [LibraryImport(Lib)]
    public static partial IntPtr objc_autoreleasePoolPush();

    [LibraryImport(Lib)]
    public static partial void objc_autoreleasePoolPop(IntPtr pool);

    public static IntPtr Send(IntPtr receiver, string selector) => SendPtr(receiver, sel_registerName(selector));

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg) => SendPtrPtr(receiver, sel_registerName(selector), arg);

    /// <summary>[[Class alloc] init] — the caller owns the result and must release it.</summary>
    public static IntPtr New(string className) => Send(Send(objc_getClass(className), "alloc"), "init");

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) Send(obj, "release");
    }

    public static string? ToManagedString(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

    public static IntPtr SharedApplication => Send(objc_getClass("NSApplication"), "sharedApplication");
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NSRange
{
    public nuint Location;
    public nuint Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public double X;
    public double Y;
    public double Width;
    public double Height;
}

internal static partial class CoreGraphics
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGRequestScreenCaptureAccess();

    [LibraryImport(Lib)]
    public static partial uint CGMainDisplayID();

    [LibraryImport(Lib)]
    public static partial CGRect CGDisplayBounds(uint display);

    [LibraryImport(Lib)]
    public static partial IntPtr CGColorSpaceCreateDeviceRGB();

    [LibraryImport(Lib)]
    public static partial void CGColorSpaceRelease(IntPtr space);

    [LibraryImport(Lib)]
    public static partial IntPtr CGBitmapContextCreate(IntPtr data, nuint width, nuint height, nuint bitsPerComponent,
        nuint bytesPerRow, IntPtr space, uint bitmapInfo);

    [LibraryImport(Lib)]
    public static partial IntPtr CGBitmapContextCreateImage(IntPtr context);

    [LibraryImport(Lib)]
    public static partial void CGContextRelease(IntPtr context);

    [LibraryImport(Lib)]
    public static partial void CGImageRelease(IntPtr image);

    /// <summary>kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little = BGRA premultiplied.</summary>
    public const uint BitmapInfoBgraPremultiplied = 2 | (2 << 12);
}
