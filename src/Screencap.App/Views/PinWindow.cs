using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Screencap.App.Services;
using Screencap.Core;

namespace Screencap.App.Views;

/// <summary>
/// A screenshot floating above all windows, inside a translucent frame. At 100 % one image
/// pixel maps to one screen pixel, regardless of the monitor's scaling.
/// </summary>
internal sealed class PinWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8;
    private const double ShadowMargin = 10;
    private const double FrameBorder = 1;
    private const double FramePadding = 6;

    /// <summary>Distance from the window edge to the image, in logical units.</summary>
    public const double ImageInset = ShadowMargin + FrameBorder + FramePadding;

    private readonly AppController _app;
    private readonly Capture _capture;
    private readonly Bitmap _bitmap;
    private readonly Image _image;
    private readonly Border _toolbar;
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private double _zoom = 1;
    private IDisposable? _labelTimer;
    private IDisposable? _toolbarTimer;

    public PinWindow(AppController app, Capture capture, Bitmap bitmap)
    {
        _app = app;
        _capture = capture;
        _bitmap = bitmap;

        Title = "Screencap – angepinnt";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _image = new Image { Source = bitmap, Stretch = Stretch.Fill };
        _labelText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 12 };
        _label = new Border
        {
            Child = _labelText,
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 16, 16, 16)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        _toolbar = CreateToolbar();

        var frame = new Border
        {
            Margin = new Thickness(ShadowMargin),
            Padding = new Thickness(FramePadding),
            CornerRadius = new CornerRadius(10),
            // Dark glass with a light rim reads well on both light and dark backgrounds.
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 40, 40, 44)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 255, 255, 255)),
            BorderThickness = new Thickness(FrameBorder),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #59000000"),
            Child = new Panel
            {
                // Keeps the toolbar usable on very small screenshots.
                MinWidth = 104,
                MinHeight = 34,
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = _image,
                    },
                    _label,
                    _toolbar,
                },
            },
        };
        frame.PointerPressed += OnFramePointerPressed;
        Content = frame;

        Cursor = new Cursor(StandardCursorType.SizeAll);
        ContextMenu = CreateContextMenu();
        UpdateSize();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (this.GetNativeHandle() is { } handle) _app.WindowPlatform.ConfigurePinned(handle);
        ScalingChanged += (_, _) => UpdateSize();
        UpdateSize();
        // Show the buttons briefly so they can be discovered, then only on hover.
        ShowToolbar(TimeSpan.FromSeconds(2));
    }

    protected override void OnClosed(EventArgs e)
    {
        _labelTimer?.Dispose();
        _toolbarTimer?.Dispose();
        _bitmap.Dispose();
        base.OnClosed(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        ShowToolbar(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ShowToolbar(TimeSpan.FromMilliseconds(400));
    }

    /// <param name="hideAfter">Null keeps it visible until the pointer leaves.</param>
    private void ShowToolbar(TimeSpan? hideAfter)
    {
        _toolbarTimer?.Dispose();
        _toolbar.Opacity = 1;
        _toolbar.IsHitTestVisible = true;
        if (hideAfter is { } delay)
            _toolbarTimer = DispatcherTimer.RunOnce(() =>
            {
                if (IsPointerOver) return;
                _toolbar.Opacity = 0;
                _toolbar.IsHitTestVisible = false;
            }, delay);
    }

    private Border CreateToolbar()
    {
        Button ToolButton(string icon, string tip, Action action)
        {
            var button = new Button
            {
                Content = new PathIcon { Data = Icons.Get(icon), Width = 13, Height = 13 },
                Padding = new Thickness(6),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => action();
            return button;
        }

        var command = AppController.IsMac ? "⌘" : "Strg+";
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 28, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }],
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 1,
                Children =
                {
                    ToolButton("IconCopy", $"Kopieren ({command}C)", () => _ = CopyAsync()),
                    ToolButton("IconEdit", "Bearbeiten (Doppelklick)", Edit),
                    ToolButton("IconClose", "Schließen (Esc)", Close),
                },
            },
        };
    }

    private void UpdateSize()
    {
        // RenderScaling = physical pixels per logical unit on the current monitor.
        var scaling = RenderScaling > 0 ? RenderScaling : 1;
        _image.Width = _bitmap.PixelSize.Width * _zoom / scaling;
        _image.Height = _bitmap.PixelSize.Height * _zoom / scaling;
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        UpdateSize();
        ShowLabel(string.Create(CultureInfo.CurrentCulture, $"{_zoom * 100:0} %"));
    }

    private void ShowLabel(string text)
    {
        _labelText.Text = text;
        _label.IsVisible = true;
        _labelTimer?.Dispose();
        _labelTimer = DispatcherTimer.RunOnce(() => _label.IsVisible = false, TimeSpan.FromSeconds(1));
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Clicks on toolbar buttons must not start a window drag.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) Edit();
        else BeginMoveDrag(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            Opacity = Math.Clamp(Opacity + Math.Sign(e.Delta.Y) * 0.1, 0.2, 1);
            ShowLabel(string.Create(CultureInfo.CurrentCulture, $"Deckkraft {Opacity * 100:0} %"));
        }
        else if (e.Delta.Y != 0)
        {
            SetZoom(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = this.CommandModifier();
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.D0 or Key.NumPad0:
                SetZoom(1);
                break;
            case Key.C when e.KeyModifiers == command:
                _ = CopyAsync();
                break;
            case Key.E or Key.Enter when e.KeyModifiers == KeyModifiers.None:
                Edit();
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    private ContextMenu CreateContextMenu()
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        return new ContextMenu
        {
            Items =
            {
                Item("Kopieren", () => _ = CopyAsync()),
                Item("Speichern", () => _ = _app.QuickSaveAsync(_capture, this)),
                Item("Bearbeiten", Edit),
                new Separator(),
                Item("Originalgröße (100 %)", () => SetZoom(1)),
                Item("Deckkraft 100 %", () => Opacity = 1),
                new Separator(),
                Item("Schließen", Close),
            },
        };
    }

    private async Task CopyAsync()
    {
        await _app.CopyAsync(_capture, this);
        ShowLabel("Kopiert");
    }

    // The pin stays open: it is often used as a reference while editing.
    private void Edit() => _app.OpenInEditor(_capture);
}
