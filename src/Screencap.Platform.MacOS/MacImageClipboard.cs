using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

/// <summary>
/// Writes images to NSPasteboard as PNG and TIFF. Many apps (Mail, Pages, Messages) read
/// TIFF, others prefer PNG; offering both makes pasting work everywhere.
/// </summary>
public sealed class MacImageClipboard : IImageClipboard
{
    public void SetImage(PixelBuffer image)
    {
        var pool = ObjC.objc_autoreleasePoolPush();
        var item = IntPtr.Zero;
        try
        {
            var png = ObjC.NSData(image.EncodePng());
            var rep = ObjC.Send(ObjC.objc_getClass("NSBitmapImageRep"), "imageRepWithData:", png);
            var tiff = rep == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(rep, "TIFFRepresentation");

            item = ObjC.New("NSPasteboardItem");
            var setData = ObjC.sel_registerName("setData:forType:");
            ObjC.SendBoolPtrPtr(item, setData, png, ObjC.NSString("public.png"));
            if (tiff != IntPtr.Zero) ObjC.SendBoolPtrPtr(item, setData, tiff, ObjC.NSString("public.tiff"));

            var pasteboard = ObjC.Send(ObjC.objc_getClass("NSPasteboard"), "generalPasteboard");
            ObjC.Send(pasteboard, "clearContents");
            var items = ObjC.Send(ObjC.objc_getClass("NSArray"), "arrayWithObject:", item);
            if (!ObjC.SendBoolPtr(pasteboard, ObjC.sel_registerName("writeObjects:"), items))
                throw new InvalidOperationException("Das Bild konnte nicht in die Zwischenablage gelegt werden.");
        }
        finally
        {
            ObjC.Release(item);
            ObjC.objc_autoreleasePoolPop(pool);
        }
    }
}
