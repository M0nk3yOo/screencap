using Avalonia;
using Avalonia.Controls;
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

    public SelectionOverlayWindow(Screen screen, PixelBuffer pixels, Bitmap image, IWindowPlatform platform)
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

        Selector = new RegionSelector(image, pixels, pixels.Width / Width);
        Content = Selector;
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
