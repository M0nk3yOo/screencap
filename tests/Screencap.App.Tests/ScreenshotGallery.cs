using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Screencap.App.Controls;
using Screencap.App.Services;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Settings;
using Screencap.Core.Updates;

namespace Screencap.App.Tests;

/// <summary>
/// Renders the pictures for the GitHub page (docs/images) from the real windows with made-up
/// sample content. Skipped unless SCREENCAP_SCREENSHOTS names the output folder:
/// <c>SCREENCAP_SCREENSHOTS=$PWD/docs/images dotnet test tests/Screencap.App.Tests --filter ScreenshotGallery</c>
/// </summary>
public sealed class ScreenshotGallery : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-gallery-{Guid.NewGuid():N}");
    private readonly List<Window> _windows = [];
    private static readonly string? OutputDirectory = Environment.GetEnvironmentVariable("SCREENCAP_SCREENSHOTS");

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private AppController NewApp(AppTheme theme = AppTheme.Light)
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.Settings.Theme = theme;
        app.Settings.GlassIntensity = 0;
        app.ApplyTheme();
        return app;
    }

    private static void Save(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing rendered");
        Directory.CreateDirectory(OutputDirectory!);
        using var file = File.Create(Path.Combine(OutputDirectory!, name));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }

    private T Show<T>(T window) where T : Window
    {
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // ---------------------------------------------------------------- sample content

    private static Border Card(string title, string value, string change, Color accent) => new()
    {
        Background = Brushes.White,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        BorderBrush = new SolidColorBrush(Color.Parse("#E5E9F0")),
        BorderThickness = new Thickness(1),
        Width = 210,
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#6B7280")) },
                new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#111827")) },
                new TextBlock { Text = change, FontSize = 12, Foreground = new SolidColorBrush(accent) },
            },
        },
    };

    /// <summary>A made-up web dashboard, drawn with real controls so text is crisp like a real screenshot.</summary>
    private static PixelBuffer SampleDashboard(int width = 980, int height = 620)
    {
        var bars = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*,*,*,*,*,*"), Height = 190, VerticalAlignment = VerticalAlignment.Bottom };
        double[] heights = [60, 85, 70, 110, 95, 130, 120, 150, 140, 165, 155, 185];
        for (var i = 0; i < heights.Length; i++)
        {
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.Parse(i == 11 ? "#2563EB" : "#BFD3FB")),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Height = heights[i],
                Margin = new Thickness(5, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetColumn(bar, i);
            bars.Children.Add(bar);
        }

        StackPanel Row(string name, string mail, string plan, string amount) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Children =
            {
                new TextBlock { Text = name, Width = 190, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = mail, Width = 260, Foreground = new SolidColorBrush(Color.Parse("#4B5563")) },
                new TextBlock { Text = plan, Width = 120, Foreground = new SolidColorBrush(Color.Parse("#4B5563")) },
                new TextBlock { Text = amount, Width = 80, TextAlignment = TextAlignment.Right },
            },
        };

        var page = new Grid
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.Parse("#F3F5F9")),
            ColumnDefinitions = new ColumnDefinitions("200,*"),
        };
        var sidebar = new StackPanel
        {
            Background = new SolidColorBrush(Color.Parse("#111827")),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Northwind", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeight.Bold, Margin = new Thickness(20, 22, 0, 18) },
                SideItem("Übersicht", true), SideItem("Kunden"), SideItem("Rechnungen"), SideItem("Berichte"), SideItem("Einstellungen"),
            },
        };
        var main = new StackPanel
        {
            Margin = new Thickness(28, 24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = "Quartalsübersicht", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#111827")) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children =
                    {
                        Card("Umsatz", "48.320 €", "▲ 12,4 % zum Vorquartal", Color.Parse("#059669")),
                        Card("Neue Kunden", "218", "▲ 8,1 %", Color.Parse("#059669")),
                        Card("Offene Rechnungen", "14", "▼ 3 überfällig", Color.Parse("#DC2626")),
                    },
                },
                new Border
                {
                    Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(16),
                    BorderBrush = new SolidColorBrush(Color.Parse("#E5E9F0")), BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = "Umsatz pro Monat", FontWeight = FontWeight.SemiBold },
                            bars,
                        },
                    },
                },
                new Border
                {
                    Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(16),
                    BorderBrush = new SolidColorBrush(Color.Parse("#E5E9F0")), BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "Letzte Zahlungen", FontWeight = FontWeight.SemiBold },
                            Row("Mara Lindner", "mara.lindner@example.com", "Team", "129,00 €"),
                            Row("Jonas Weber", "jonas.weber@example.com", "Business", "349,00 €"),
                        },
                    },
                },
            },
        };
        Grid.SetColumn(main, 1);
        page.Children.Add(sidebar);
        page.Children.Add(main);
        return Render(page);
    }

    private static Control SideItem(string text, bool active = false) => new Border
    {
        Background = active ? new SolidColorBrush(Color.Parse("#1F2A44")) : Brushes.Transparent,
        Padding = new Thickness(20, 9),
        Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse(active ? "#FFFFFF" : "#9CA3AF")) },
    };

    private static PixelBuffer Render(Control control)
    {
        var size = new Size(control.Width, control.Height);
        control.Measure(size);
        control.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        bitmap.Render(control);
        return bitmap.ToPixelBuffer();
    }

    // ---------------------------------------------------------------- the pictures

    private static bool Skip => string.IsNullOrEmpty(OutputDirectory);

    private static Capture AnnotatedCapture()
    {
        var capture = new Capture(SampleDashboard(), 1);
        var red = new ArgbColor(255, 235, 64, 52);
        var blue = new ArgbColor(255, 37, 99, 235);
        var yellow = new ArgbColor(255, 255, 214, 10);
        capture.Annotations.Add(new HighlightAnnotation { Rect = new RectD(236, 98, 152, 36), Color = yellow.WithAlpha(110) });
        capture.Annotations.Add(new RectangleAnnotation { Rect = new RectD(222, 60, 222, 112), Color = red, StrokeWidth = 4 });
        capture.Annotations.Add(new ArrowAnnotation { Start = new PointD(560, 60), End = new PointD(452, 96), Color = red, StrokeWidth = 5 });
        capture.Annotations.Add(new TextAnnotation
        {
            Position = new PointD(470, 14), Text = "Hier steht der Umsatz", FontSize = 24, Color = red, Bold = true,
        });
        capture.Annotations.Add(new CounterAnnotation { Center = new PointD(906, 204), Number = 1, Color = blue });
        capture.Annotations.Add(new CounterAnnotation { Center = new PointD(646, 494), Number = 2, Color = blue });
        capture.Annotations.Add(new PixelateAnnotation { Rect = new RectD(430, 484, 160, 20), Style = ObscureStyle.Blur, BlurRadius = 6 });
        capture.Annotations.Add(new PixelateAnnotation { Rect = new RectD(430, 504, 160, 20), Style = ObscureStyle.Pixelate, BlockSize = 7 });
        return capture;
    }

    [AvaloniaFact]
    public void Editor_Light()
    {
        if (Skip) return;
        var app = NewApp();
        var capture = AnnotatedCapture();
        app.OpenInEditor(capture);
        var main = Show(app.ShowMainWindow());
        main.Width = 1240;
        main.Height = 820;
        main.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Save(main, "editor-hell.png");
    }

    [AvaloniaFact]
    public void Editor_Dark()
    {
        if (Skip) return;
        var app = NewApp(AppTheme.Dark);
        app.OpenInEditor(AnnotatedCapture());
        var main = Show(app.ShowMainWindow());
        main.Width = 1240;
        main.Height = 820;
        main.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Save(main, "editor-dunkel.png");
    }

    [AvaloniaFact]
    public void Settings()
    {
        if (Skip) return;
        var app = NewApp();
        // The updates section only exists in the installed app; pretend to be it.
        var http = new HttpClient();
        app.Updates = new UpdateService(new UpdateChecker(http), new UpdateInstaller(http), UpdateTarget.WindowsX64,
            Path.Combine(_dataDirectory, "Screencap.exe"), AppContext.BaseDirectory);
        var window = Show(new SettingsWindow(app));
        Save(window, "einstellungen.png");
    }

    [AvaloniaFact]
    public void UpdateDialog()
    {
        if (Skip) return;
        var app = NewApp();
        var release = new ReleaseInfo(new Version(1, 1, 0), "v1.1.0",
            "- Neu: Beispiel-Eintrag aus den Versionshinweisen\n- Behoben: ein weiterer Eintrag", "", []);
        var window = Show(new UpdateWindow(app, release));
        Save(window, "update.png");
    }

    [AvaloniaFact]
    public void RegionSelection()
    {
        if (Skip) return;
        var pixels = SampleDashboard(1280, 800);
        using var bitmap = pixels.ToBitmap();
        var selector = new RegionSelector(bitmap, pixels, 1);
        var window = Show(new Window { Content = selector, Width = 1280, Height = 800, WindowDecorations = WindowDecorations.None });
        window.MouseMove(new Point(222, 60));
        window.MouseDown(new Point(222, 60), MouseButton.Left);
        window.MouseMove(new Point(895, 172));
        Save(window, "bereich-auswaehlen.png");
    }
}
