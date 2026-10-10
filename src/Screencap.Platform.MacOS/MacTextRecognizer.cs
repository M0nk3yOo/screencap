using System.Runtime.InteropServices;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

/// <summary>OCR via Apple's Vision framework (VNRecognizeTextRequest), fully on-device.</summary>
public sealed class MacTextRecognizer : ITextRecognizer
{
    private const nint RecognitionLevelAccurate = 0;
    private static readonly Lazy<bool> VisionLoaded = new(() =>
        NativeLibrary.TryLoad("/System/Library/Frameworks/Vision.framework/Vision", out _));

    public Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(PixelBuffer image, CancellationToken cancellationToken = default) =>
        Task.Run(() => Recognize(image), cancellationToken);

    private static unsafe IReadOnlyList<RecognizedLine> Recognize(PixelBuffer image)
    {
        if (!VisionLoaded.Value) throw new PlatformNotSupportedException("Vision framework not available");

        var pool = ObjC.objc_autoreleasePoolPush();
        IntPtr colorSpace = IntPtr.Zero, context = IntPtr.Zero, cgImage = IntPtr.Zero, handler = IntPtr.Zero, request = IntPtr.Zero;
        try
        {
            fixed (byte* pixels = image.Data)
            {
                colorSpace = CoreGraphics.CGColorSpaceCreateDeviceRGB();
                context = CoreGraphics.CGBitmapContextCreate((IntPtr)pixels, (nuint)image.Width, (nuint)image.Height, 8,
                    (nuint)image.Stride, colorSpace, CoreGraphics.BitmapInfoBgraPremultiplied);
                cgImage = CoreGraphics.CGBitmapContextCreateImage(context);
                if (cgImage == IntPtr.Zero) throw new InvalidOperationException("CGImage could not be created");

                var options = ObjC.Send(ObjC.objc_getClass("NSDictionary"), "dictionary");
                handler = ObjC.SendPtrPtrPtr(ObjC.Send(ObjC.objc_getClass("VNImageRequestHandler"), "alloc"),
                    ObjC.sel_registerName("initWithCGImage:options:"), cgImage, options);

                request = ObjC.New("VNRecognizeTextRequest");
                ObjC.SendVoidLong(request, ObjC.sel_registerName("setRecognitionLevel:"), RecognitionLevelAccurate);
                ObjC.SendVoidBool(request, ObjC.sel_registerName("setUsesLanguageCorrection:"), true);
                var detectLanguage = ObjC.sel_registerName("setAutomaticallyDetectsLanguage:");
                if (ObjC.SendBoolPtr(request, ObjC.sel_registerName("respondsToSelector:"), detectLanguage))
                    ObjC.SendVoidBool(request, detectLanguage, true); // macOS 13+

                var requests = ObjC.Send(ObjC.objc_getClass("NSArray"), "arrayWithObject:", request);
                if (!ObjC.SendBoolPtrOut(handler, ObjC.sel_registerName("performRequests:error:"), requests, out var error))
                    throw new InvalidOperationException(
                        ObjC.ToManagedString(ObjC.Send(error, "localizedDescription")) ?? "Text recognition failed");

                return ReadResults(ObjC.Send(request, "results"), image.Width, image.Height);
            }
        }
        finally
        {
            ObjC.Release(request);
            ObjC.Release(handler);
            if (cgImage != IntPtr.Zero) CoreGraphics.CGImageRelease(cgImage);
            if (context != IntPtr.Zero) CoreGraphics.CGContextRelease(context);
            if (colorSpace != IntPtr.Zero) CoreGraphics.CGColorSpaceRelease(colorSpace);
            ObjC.objc_autoreleasePoolPop(pool);
        }
    }

    private static List<RecognizedLine> ReadResults(IntPtr results, int width, int height)
    {
        var lines = new List<RecognizedLine>();
        if (results == IntPtr.Zero) return lines;
        var count = ObjC.SendULong(results, ObjC.sel_registerName("count"));
        for (nuint i = 0; i < count; i++)
        {
            var observation = ObjC.SendPtrULong(results, ObjC.sel_registerName("objectAtIndex:"), i);
            var candidates = ObjC.SendPtrULong(observation, ObjC.sel_registerName("topCandidates:"), 1);
            if (ObjC.SendULong(candidates, ObjC.sel_registerName("count")) == 0) continue;
            var candidate = ObjC.SendPtrULong(candidates, ObjC.sel_registerName("objectAtIndex:"), 0);
            var text = ObjC.ToManagedString(ObjC.Send(candidate, "string"));
            if (string.IsNullOrEmpty(text)) continue;

            var box = ToImageRect(ObjC.SendRect(observation, "boundingBox"), width, height);
            lines.Add(new RecognizedLine(text, box, ReadWords(candidate, text, width, height)));
        }
        return lines;
    }

    /// <summary>Vision reports lines; word boxes come from boundingBoxForRange on each word's range.</summary>
    private static List<RecognizedWord> ReadWords(IntPtr candidate, string text, int width, int height)
    {
        var words = new List<RecognizedWord>();
        var selector = ObjC.sel_registerName("boundingBoxForRange:error:");
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isSpace = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!isSpace && start < 0) start = i;
            if (!isSpace || start < 0) continue;

            // NSRange uses UTF-16 offsets, the same as .NET string indices.
            var range = new NSRange { Location = (nuint)start, Length = (nuint)(i - start) };
            var rectangle = ObjC.SendPtrRangeOut(candidate, selector, range, out _);
            if (rectangle != IntPtr.Zero)
                words.Add(new RecognizedWord(text[start..i], ToImageRect(ObjC.SendRect(rectangle, "boundingBox"), width, height)));
            start = -1;
        }
        return words;
    }

    /// <summary>Vision boxes are normalized with a bottom-left origin.</summary>
    private static RectD ToImageRect(CGRect box, int width, int height) =>
        new(box.X * width, (1 - box.Y - box.Height) * height, box.Width * width, box.Height * height);
}
