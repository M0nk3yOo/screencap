using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Geometry;
using Screencap.Core.Ocr;

namespace Screencap.App.Views;

/// <summary>
/// Result of "recognize text in region": the text (already on the clipboard), editable,
/// shown next to the marked region like the screenshot preview.
/// </summary>
internal sealed class TextResultWindow : Window
{
    private const double Gap = 12;

    private readonly AppController _app;
    private readonly Capture _capture;
    private readonly PixelRect _region;
    private readonly Screen _screen;
    private readonly TextBox _text;
    private readonly TextBlock _status;

    public TextResultWindow(AppController app, Capture capture, string text, IReadOnlyList<DecodedCode> codes,
        PixelRect region, Screen screen)
    {
        _app = app;
        _capture = capture;
        _region = region;
        _screen = screen;

        Title = "Erkannter Text";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Opacity = 0;

        // A QR code in the region is what the user is after; its text comes first.
        var combined = string.Join("\n\n", codes.Select(c => c.Text).Append(codes.Count > 0 ? "" : text).Where(t => t.Length > 0));
        var link = codes.FirstOrDefault(c => c.IsLink)?.Text.Trim();
        _text = new TextBox
        {
            Text = combined,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 320,
            MinHeight = 60,
            FontSize = 13,
        };
        _status = new TextBlock
        {
            Text = combined.Length == 0 ? "Kein Text gefunden." : "In die Zwischenablage kopiert.",
            FontSize = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var copy = new Button { Content = "Kopieren", Classes = { "accent" } };
        var openLink = new Button { Content = "Link öffnen", IsVisible = link is not null };
        openLink.Click += (_, _) =>
        {
            AppController.OpenLink(link!);
            Close();
        };
        copy.Click += async (_, _) => await CopyAsync();
        var edit = new Button { Content = "Im Editor öffnen" };
        edit.Click += (_, _) =>
        {
            _app.OpenInEditor(_capture);
            Close();
        };
        var close = new Button
        {
            Classes = { "flat" },
            Content = new PathIcon { Data = Icons.Get("IconClose"), Width = 11, Height = 11 },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = codes.Count > 0 ? (codes.Count == 1 ? $"{codes[0].Format} erkannt" : $"{codes.Count} Codes erkannt") : "Erkannter Text";
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        buttons.Children.Add(_status);
        Grid.SetColumn(edit, 1);
        Grid.SetColumn(openLink, 2);
        Grid.SetColumn(copy, 3);
        buttons.Children.Add(edit);
        buttons.Children.Add(openLink);
        buttons.Children.Add(copy);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x23, 0x24, 0x27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 12),
            Child = new StackPanel { Spacing = 8, Children = { header, _text, buttons } },
        };

        PointerPressed += (_, e) =>
        {
            if (e.Source is not TextBox && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Dispatcher.UIThread.Post(async () =>
        {
            var scaling = _screen.Scaling;
            var work = _screen.WorkingArea;
            var position = PreviewPlacement.Place(
                new RectD(_region.X, _region.Y, _region.Width, _region.Height),
                new SizeD(Bounds.Width * scaling, Bounds.Height * scaling),
                new RectD(work.X, work.Y, work.Width, work.Height), Gap * scaling);
            Position = new PixelPoint((int)position.X, (int)position.Y);
            Opacity = 1;
            _app.WindowPlatform.ActivateApplication();
            Activate();
            if (!string.IsNullOrEmpty(_text.Text)) await CopyAsync();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private async Task CopyAsync()
    {
        if (Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(_text.Text ?? "");
        _status.Text = "In die Zwischenablage kopiert.";
    }
}
