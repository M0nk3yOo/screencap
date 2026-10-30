using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Screencap.Core.Settings;

namespace Screencap.App.Services;

internal static class TrayMenu
{
    public static TrayIcon Create(AppController app)
    {
        var updateItem = Item("Nach Updates suchen …", () => _ = ShowUpdatesAsync(app));
        if (!app.Updates.CanInstall) updateItem.IsVisible = false;
        app.Updates.AvailableChanged += () =>
            updateItem.Header = app.Updates.Available is { } r ? $"Version {r.Version.ToString(3)} installieren …" : "Nach Updates suchen …";
        var menu = new NativeMenu
        {
            Item("Bereich aufnehmen", () => _ = app.CaptureRegionAsync(), app.Settings.RegionHotkey),
            Item("Vollbild aufnehmen", () => _ = app.CaptureFullscreenAsync(), app.Settings.FullscreenHotkey),
            Item("Scroll-Aufnahme …", () => _ = app.CaptureScrollingAsync(), app.Settings.ScrollHotkey),
            Item("Text erkennen …", () => _ = app.CaptureTextAsync(), app.Settings.OcrHotkey),
            new NativeMenuItemSeparator(),
            Item("Hauptfenster öffnen", () => app.ShowMainWindow()),
            Item("Einstellungen …", app.ShowSettings),
            updateItem,
            new NativeMenuItemSeparator(),
            Item("Screencap beenden", app.Quit),
        };

        var tray = new TrayIcon
        {
            Icon = CreateIcon(AppController.IsMac),
            ToolTipText = "Screencap",
            Menu = menu,
        };
        // macOS tints template icons to match the menu bar (light/dark).
        MacOSProperties.SetIsTemplateIcon(tray, AppController.IsMac);
        tray.Clicked += (_, _) => app.ShowMainWindow();
        TrayIcon.SetIcons(Application.Current!, [tray]);
        return tray;
    }

    private static async Task ShowUpdatesAsync(AppController app)
    {
        if (app.Updates.Available is { } release) app.ShowUpdateWindow(release);
        else await app.CheckForUpdatesAsync(userInitiated: true);
    }

    private static NativeMenuItem Item(string header, Action action, Hotkey? hotkey = null)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        if (hotkey is not null && ToKeyGesture(hotkey) is { } gesture) item.Gesture = gesture;
        return item;
    }

    /// <summary>Only shown next to the menu entry; the global hotkey itself is registered natively.</summary>
    private static KeyGesture? ToKeyGesture(Hotkey hotkey)
    {
        var key = HotkeyMapping.ToKey(hotkey.Key);
        return key == Key.None ? null : new KeyGesture(key, HotkeyMapping.ToKeyModifiers(hotkey.Modifiers));
    }

    /// <summary>
    /// A viewfinder glyph. Black template on macOS; on Windows a green tile, because a
    /// black glyph would vanish on the dark taskbar.
    /// </summary>
    private static WindowIcon CreateIcon(bool template)
    {
        const int size = 64;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var dc = bitmap.CreateDrawingContext())
        {
            IBrush glyph = Brushes.Black;
            if (!template)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.Parse("#059669")), null, new Rect(0, 0, size, size), 14, 14);
                glyph = Brushes.White;
            }

            var inset = template ? 8.0 : 14.0;
            var corner = template ? 16.0 : 12.0;
            var pen = new Pen(glyph, template ? 6 : 5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var (l, t, r, b) = (inset, inset, size - inset, size - inset);
            var corners = new StreamGeometry();
            using (var ctx = corners.Open())
            {
                Polyline(ctx, new(l, t + corner), new(l, t), new(l + corner, t));
                Polyline(ctx, new(r - corner, t), new(r, t), new(r, t + corner));
                Polyline(ctx, new(r, b - corner), new(r, b), new(r - corner, b));
                Polyline(ctx, new(l + corner, b), new(l, b), new(l, b - corner));
            }
            dc.DrawGeometry(null, pen, corners);
            dc.DrawEllipse(glyph, null, new Point(size / 2.0, size / 2.0), template ? 8 : 6, template ? 8 : 6);
        }

        var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private static void Polyline(StreamGeometryContext ctx, Point a, Point b, Point c)
    {
        ctx.BeginFigure(a, false);
        ctx.LineTo(b);
        ctx.LineTo(c);
        ctx.EndFigure(false);
    }
}
