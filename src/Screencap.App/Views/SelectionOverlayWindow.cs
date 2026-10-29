using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.App.Views;

/// <summary>Covers exactly one screen with its frozen screenshot. One instance per monitor.</summary>
internal sealed class SelectionOverlayWindow : Window
{
    private readonly IWindowPlatform _platform;

    public SelectionOverlayWindow(Screen screen, PixelBuffer pixels, Bitmap image, IWindowPlatform platform,
        IReadOnlyList<WindowInfo>? windows = null)
    {
        _platform = platform;
        Screen = screen;

        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = screen.Bounds.Position;
        // Bounds are in the toolkit's pixel space; window sizes are logical units.
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;

        // Window bounds come in screen coordinates; the selector works in this window's logical units.
        var b = screen.Bounds;
        var local = (windows ?? [])
            .Select(w => (Bounds: new Rect((w.Bounds.X - b.X) / screen.Scaling, (w.Bounds.Y - b.Y) / screen.Scaling,
                w.Bounds.Width / screen.Scaling, w.Bounds.Height / screen.Scaling),
                Label: string.IsNullOrEmpty(w.Title) || w.Title == w.App ? w.App : $"{w.App} – {w.Title}"))
            .Where(w => w.Bounds.Width >= 40 && w.Bounds.Height >= 30 && w.Bounds.Intersects(new Rect(0, 0, Width, Height)))
            .ToList();
        Selector = new RegionSelector(image, pixels, pixels.Width / Width, local);
        Content = Selector;

        // Esc cancels wherever the keyboard focus is; with several overlays only one has it.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Selector.Cancel();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        // The pointer is on this screen, so this overlay should get the keys.
        Selector.PointerEntered += (_, _) =>
        {
            Activate();
            Selector.Focus();
        };
    }

    public Screen Screen { get; }

    public RegionSelector Selector { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var b = Screen.Bounds;
        if (this.GetNativeHandle() is { } handle)
            _platform.ConfigureOverlay(handle, new IntRect(b.X, b.Y, b.Width, b.Height));
        _platform.ActivateApplication();
        Activate();
        Selector.Focus();
    }
}
