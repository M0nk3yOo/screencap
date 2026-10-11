using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Platform;

namespace Screencap.App.Views;

/// <summary>
/// Scroll capture: frames the region, captures it repeatedly while the user scrolls and
/// stitches the frames. Its own windows are excluded from the capture.
/// </summary>
internal sealed class ScrollCaptureWindow : Window
{
    private const double Gap = 12;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    private readonly IPlatformServices _platform;
    private readonly PixelRect _region;
    private readonly Screen _screen;
    private readonly ScrollStitcher _stitcher = new();
    private readonly List<Window> _frame = [];
    private readonly TextBlock _status;
    private readonly TaskCompletionSource<PixelBuffer?> _result = new();
    private DispatcherTimer? _timer;
    private bool _busy;
    private int _firstWidth;
    private int _firstHeight;

    public ScrollCaptureWindow(IPlatformServices platform, PixelRect region, Screen screen)
    {
        _platform = platform;
        _region = region;
        _screen = screen;

        Title = "Scroll-Aufnahme";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Opacity = 0;

        _status = new TextBlock { Text = "Bereit …", FontSize = 12, Opacity = 0.75 };
        var done = new Button { Content = "Fertig", Classes = { "accent" } };
        done.Click += (_, _) => Finish();
        var cancel = new Button { Content = "Abbrechen" };
        cancel.Click += (_, _) => Cancel();

        var frame = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 6,
                Width = 270,
                Children =
                {
                    new TextBlock { Text = "Scroll-Aufnahme", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Jetzt im markierten Bereich langsam nach unten scrollen.", TextWrapping = TextWrapping.Wrap, FontSize = 12 },
                    _status,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, done } },
                },
            },
        };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("ScHudBrush"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("ScHudBorderBrush"));
        Content = frame;
    }

    /// <summary>Runs until "Fertig" (stitched image) or "Abbrechen" (null).</summary>
    public Task<PixelBuffer?> RunAsync()
    {
        Show();
        return _result.Task;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (this.GetNativeHandle() is { } handle) _platform.Windows.ExcludeFromCapture(handle);
        ShowRegionFrame();
        Dispatcher.UIThread.Post(() =>
        {
            var scaling = _screen.Scaling;
            var work = _screen.WorkingArea;
            var position = PreviewPlacement.Place(new RectD(_region.X, _region.Y, _region.Width, _region.Height),
                new SizeD(Bounds.Width * scaling, Bounds.Height * scaling),
                new RectD(work.X, work.Y, work.Width, work.Height), Gap * scaling);
            Position = new PixelPoint((int)position.X, (int)position.Y);
            Opacity = 1;
        }, DispatcherPriority.Loaded);

        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, async (_, _) => await CaptureFrameAsync());
        _timer.Start();
        _ = CaptureFrameAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
        else if (e.Key == Key.Enter) Finish();
        else base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        foreach (var w in _frame) w.Close();
        _result.TrySetResult(null);
        base.OnClosed(e);
    }

    private async Task CaptureFrameAsync()
    {
        if (_busy || _result.Task.IsCompleted) return;
        _busy = true;
        try
        {
            var region = new IntRect(_region.X, _region.Y, _region.Width, _region.Height);
            var frame = await _platform.Capturer.CaptureAsync(new ScreenInfo(region, _screen.Scaling));
            if (_stitcher.FrameCount == 0)
            {
                _firstWidth = frame.Width;
                _firstHeight = frame.Height;
            }
            else if (frame.Width != _firstWidth || frame.Height != _firstHeight)
            {
                // Rounding can make a capture one pixel larger; keep every frame the same size.
                frame = frame.Crop(new IntRect(0, 0, Math.Min(frame.Width, _firstWidth), Math.Min(frame.Height, _firstHeight)));
                if (frame.Width != _firstWidth || frame.Height != _firstHeight) return;
            }
            _stitcher.Add(frame);
            _status.Text = $"{_stitcher.FrameCount} {(_stitcher.FrameCount == 1 ? "Bild" : "Bilder")} · {_stitcher.Height} px hoch";
            if (_stitcher.Height >= ScrollStitcher.MaxHeight) _status.Text += " (Maximum erreicht)";
        }
        catch (Exception e)
        {
            CrashLog.Write("Scroll capture", e);
            _status.Text = "Aufnahme fehlgeschlagen.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Finish()
    {
        _timer?.Stop();
        while (_busy) await Task.Delay(20);
        _result.TrySetResult(_stitcher.FrameCount > 0 ? _stitcher.Result() : null);
        Close();
    }

    private void Cancel()
    {
        _result.TrySetResult(null);
        Close();
    }

    /// <summary>Four thin bars just outside the region show what is being recorded.</summary>
    private void ShowRegionFrame()
    {
        var scaling = _screen.Scaling;
        var thickness = (int)Math.Ceiling(3 * scaling);
        var r = _region;
        PixelRect[] bars =
        [
            new(r.X - thickness, r.Y - thickness, r.Width + 2 * thickness, thickness),
            new(r.X - thickness, r.Bottom, r.Width + 2 * thickness, thickness),
            new(r.X - thickness, r.Y, thickness, r.Height),
            new(r.Right, r.Y, thickness, r.Height),
        ];
        foreach (var bar in bars)
        {
            var window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = bar.Position,
                Width = Math.Max(1, bar.Width / scaling),
                Height = Math.Max(1, bar.Height / scaling),
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                IsHitTestVisible = false,
            };
            window.Opened += (_, _) =>
            {
                if (window.GetNativeHandle() is { } handle) _platform.Windows.ExcludeFromCapture(handle);
            };
            _frame.Add(window);
            window.Show();
        }
    }
}
