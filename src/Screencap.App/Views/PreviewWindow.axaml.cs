using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Geometry;

namespace Screencap.App.Views;

/// <summary>Small window shown next to the captured region right after a capture.</summary>
public partial class PreviewWindow : Window
{
    private const double MaxThumbnailWidth = 340;
    private const double MaxThumbnailHeight = 240;
    private const double MinThumbnailWidth = 260;
    private const double Gap = 12;

    private readonly AppController _app;
    private readonly Capture _capture;
    private readonly PixelRect _region;
    private readonly Screen _screen;
    private IDisposable? _toastTimer;

    // Required by the XAML loader for previews; not used at runtime.
    public PreviewWindow() : this(null!, null!, default, null!) { }

    internal PreviewWindow(AppController app, Capture capture, PixelRect region, Screen screen)
    {
        InitializeComponent();
        _app = app;
        _capture = capture;
        _region = region;
        _screen = screen;
        if (capture is null) return;

        Thumbnail.Source = app.GetBitmap(capture);
        var (width, height) = ThumbnailSize(capture);
        Thumbnail.Width = width;
        Thumbnail.Height = height;
        ImageHost.MinWidth = MinThumbnailWidth;

        var command = AppController.IsMac ? "⌘" : "Strg+";
        CopyButton.SetValue(ToolTip.TipProperty, $"Kopieren und schließen ({command}C)");
        SaveButton.SetValue(ToolTip.TipProperty, $"Speichern ({command}S)");
        OcrButton.SetValue(ToolTip.TipProperty, "Text erkennen und kopieren");

        EditButton.Click += (_, _) => Edit();
        CopyButton.Click += async (_, _) => await CopyAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        OcrButton.Click += async (_, _) => await RecognizeTextAsync();
        PinButton.Click += (_, _) => Pin();
        CloseButton.Click += (_, _) => Close();
        ImageHost.PointerPressed += OnImagePointerPressed;
    }

    /// <summary>Logical size of the screenshot, shrunk to fit the preview.</summary>
    private static (double Width, double Height) ThumbnailSize(Capture capture)
    {
        var width = capture.Image.Width / capture.Scaling;
        var height = capture.Image.Height / capture.Scaling;
        var scale = Math.Min(1, Math.Min(MaxThumbnailWidth / width, MaxThumbnailHeight / height));
        return (Math.Max(1, width * scale), Math.Max(1, height * scale));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // The final size is only known after layout, so position first and then fade in.
        Dispatcher.UIThread.Post(() =>
        {
            PlaceNextToRegion();
            Opacity = 1;
            _app.WindowPlatform.ActivateApplication();
            Activate();
            Focus();
        }, DispatcherPriority.Loaded);
    }

    private void PlaceNextToRegion()
    {
        var scaling = _screen.Scaling;
        var size = new SizeD(Bounds.Width * scaling, Bounds.Height * scaling);
        var work = _screen.WorkingArea;
        var position = PreviewPlacement.Place(
            new RectD(_region.X, _region.Y, _region.Width, _region.Height),
            size,
            new RectD(work.X, work.Y, work.Width, work.Height),
            Gap * scaling);
        Position = new PixelPoint((int)Math.Round(position.X), (int)Math.Round(position.Y));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = this.CommandModifier();
        var handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter or Key.E when e.KeyModifiers == KeyModifiers.None:
                Edit();
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.None:
                Pin();
                break;
            case Key.C when e.KeyModifiers == command:
                _ = CopyAsync();
                break;
            case Key.S when e.KeyModifiers == command:
                _ = SaveAsync();
                break;
            default:
                handled = false;
                break;
        }
        e.Handled = handled;
        if (!handled) base.OnKeyDown(e);
    }

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) Edit();
        else BeginMoveDrag(e);
    }

    private void Edit()
    {
        _app.OpenInEditor(_capture);
        Close();
    }

    private void Pin()
    {
        // The image inside the pin lands exactly where the screenshot was taken.
        var inset = (int)Math.Round(PinWindow.ImageInset * _screen.Scaling);
        _app.Pin(_capture, new PixelPoint(_region.X - inset, _region.Y - inset));
        Close();
    }

    /// <summary>Copying finishes the job, so the preview closes (like the pin).</summary>
    private async Task CopyAsync()
    {
        try
        {
            await _app.CopyAsync(_capture, this);
            Close();
        }
        catch (Exception e)
        {
            CrashLog.Write("Copy (preview)", e);
            ShowToast("Kopieren fehlgeschlagen");
        }
    }

    private async Task RecognizeTextAsync()
    {
        ShowToast("Text wird erkannt …");
        try
        {
            var text = await _app.RecognizeTextAsync(_capture);
            if (text.Length == 0)
            {
                ShowToast("Kein Text gefunden");
                return;
            }
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            var lines = text.Count(c => c == '\n') + 1;
            ShowToast($"Text kopiert ({lines} {(lines == 1 ? "Zeile" : "Zeilen")})");
        }
        catch (Exception ex)
        {
            CrashLog.Write("OCR", ex);
            ShowToast("Texterkennung fehlgeschlagen");
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            if (await _app.QuickSaveAsync(_capture, this) is { } path)
                ShowToast($"Gespeichert: {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("Speichern fehlgeschlagen");
            Console.Error.WriteLine(ex);
        }
    }

    internal void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.IsVisible = true;
        _toastTimer?.Dispose();
        _toastTimer = DispatcherTimer.RunOnce(() => Toast.IsVisible = false, TimeSpan.FromSeconds(1.4));
    }

    protected override void OnClosed(EventArgs e)
    {
        _toastTimer?.Dispose();
        base.OnClosed(e);
    }
}
