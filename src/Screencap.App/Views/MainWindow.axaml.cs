using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Screencap.App.Services;
using Screencap.Core;

namespace Screencap.App.Views;

/// <summary>The editor window. Each screenshot gets a tab in the title bar and its own editor.</summary>
public partial class MainWindow : Window
{
    private sealed class Tab(Capture capture, EditorView editor)
    {
        public Capture Capture { get; } = capture;
        public EditorView Editor { get; } = editor;
        public Border Chip { get; set; } = null!;
    }

    private readonly AppController _app;
    private readonly List<Tab> _tabs = [];
    private readonly Button _newCaptureButton;
    private Tab? _selected;
    private Tab? _draggedTab;
    private Point _dragStartPoint;
    private bool _isDraggingTab;

    // Required by the XAML loader for previews; not used at runtime.
    public MainWindow() : this(null!) { }

    internal MainWindow(AppController app)
    {
        InitializeComponent();
        Icon = AppIcon.Window;
        _app = app;
        _newCaptureButton = CreateNewCaptureButton();
        if (app is null) return;

        if (AppController.IsMac)
        {
            // Tabs live in the title bar, next to the traffic lights.
            LeftInset.Width = 78;
            RightInset.Width = 8;
            WindowDecorationProperties.SetElementRole(DragArea, WindowDecorationsElementRole.TitleBar);
        }
        else
        {
            // Windows keeps its native title bar: the extended, Avalonia-drawn one adds a title
            // and a full-screen button that overlapped the tabs. The tabs get their own row.
            ExtendClientAreaToDecorationsHint = false;
            TitleBar.Height = 42;
            LeftInset.Width = 6;
            RightInset.Width = 6;
        }

        EmptyHint.Text = $"Bereich aufnehmen mit {app.FormatHotkey(app.Settings.RegionHotkey)}, " +
                         $"Vollbild mit {app.FormatHotkey(app.Settings.FullscreenHotkey)}";
        CaptureButton.Click += (_, _) => _ = app.CaptureIntoNewTabAsync();
        SettingsButton.Click += (_, _) => app.ShowSettings();

        TabStrip.Children.Add(_newCaptureButton);
        foreach (var capture in app.Tabs) AddTab(capture);
        app.Tabs.CollectionChanged += OnTabsChanged;
        if (_tabs.Count > 0) Select(_tabs[^1]);
        UpdateEmptyState();

        ApplyGlass();
        ActualThemeVariantChanged += (_, _) => ApplyGlass();
        app.SettingsChanged += ApplyGlass;

        // Tunnel so tool shortcuts work no matter which toolbar control has focus.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Open screenshots in tab order (for tests and keyboard navigation).</summary>
    internal IReadOnlyList<Capture> OpenTabs => _tabs.Select(t => t.Capture).ToList();

    internal Capture? SelectedCapture => _selected?.Capture;

    internal void SelectTab(Capture capture)
    {
        if (_tabs.FirstOrDefault(t => t.Capture == capture) is { } tab) Select(tab);
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.Tabs.CollectionChanged -= OnTabsChanged;
        _app.SettingsChanged -= ApplyGlass;
        base.OnClosed(e);
    }

    private Button CreateNewCaptureButton()
    {
        var button = new Button
        {
            Classes = { "flat" },
            Content = new PathIcon { Data = Icons.Get("IconPlus"), Width = 14, Height = 14 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, AppController.IsMac ? "Neue Aufnahme (⌘N)" : "Neue Aufnahme (Strg+N)");
        button.Click += (_, _) => _ = _app.CaptureIntoNewTabAsync();
        return button;
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            var moved = _tabs[e.OldStartingIndex];
            _tabs.RemoveAt(e.OldStartingIndex);
            _tabs.Insert(e.NewStartingIndex, moved);
            // Re-insert the neighbour rather than the dragged chip: removing the dragged chip
            // from the tree would cancel its pointer capture and end the drag.
            var displaced = _tabs[e.OldStartingIndex];
            TabStrip.Children.Remove(displaced.Chip);
            TabStrip.Children.Insert(e.OldStartingIndex, displaced.Chip);
            return;
        }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in _tabs.ToList()) RemoveTab(tab);
        }
        foreach (Capture capture in e.OldItems ?? Array.Empty<Capture>())
            if (_tabs.FirstOrDefault(t => t.Capture == capture) is { } tab) RemoveTab(tab);
        foreach (Capture capture in e.NewItems ?? Array.Empty<Capture>())
            Select(AddTab(capture));
        UpdateEmptyState();
    }

    private Tab AddTab(Capture capture)
    {
        var editor = new EditorView(_app, capture) { IsVisible = false };
        ContentHost.Children.Add(editor);
        var tab = new Tab(capture, editor);
        tab.Chip = CreateChip(tab);
        _tabs.Add(tab);
        TabStrip.Children.Insert(TabStrip.Children.Count - 1, tab.Chip);
        return tab;
    }

    private void RemoveTab(Tab tab)
    {
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        TabStrip.Children.Remove(tab.Chip);
        ContentHost.Children.Remove(tab.Editor);
        if (_selected == tab)
        {
            _selected = null;
            if (_tabs.Count > 0) Select(_tabs[Math.Clamp(index, 0, _tabs.Count - 1)]);
        }
        UpdateTitle();
    }

    private void Select(Tab tab)
    {
        _selected = tab;
        foreach (var t in _tabs)
        {
            t.Chip.Classes.Set("selected", t == tab);
            t.Editor.IsVisible = t == tab;
        }
        tab.Chip.BringIntoView();
        UpdateTitle();
    }

    private Border CreateChip(Tab tab)
    {
        var capture = tab.Capture;
        var dirtyDot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.Parse("#FF9500")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(dirtyDot, "Ungespeicherte Änderungen");

        var closeButton = new Button
        {
            Classes = { "tabClose" },
            Content = new PathIcon { Data = Icons.Get("IconClose"), Width = 9, Height = 9 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(closeButton, AppController.IsMac ? "Schließen (⌘W)" : "Schließen (Strg+W)");
        closeButton.Click += (_, _) => _app.CloseTab(capture);

        var chip = new Border
        {
            Classes = { "tab" },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new Image { Source = _app.GetBitmap(capture), Height = 22, MaxWidth = 38, Stretch = Stretch.UniformToFill },
                    },
                    new TextBlock { Text = capture.CreatedAt.ToString("HH:mm:ss"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 },
                    dirtyDot,
                    closeButton,
                },
            },
        };
        var source = capture.SourceDescription is { } description ? $"{description}\n" : "";
        ToolTip.SetTip(chip, $"{source}{capture.CreatedAt:dd.MM.yyyy HH:mm:ss} · {capture.Image.Width} × {capture.Image.Height} px");
        chip.ContextMenu = CreateTabMenu(capture);
        chip.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
            var properties = e.GetCurrentPoint(chip).Properties;
            if (properties.IsMiddleButtonPressed)
            {
                _app.CloseTab(capture);
                return;
            }
            if (!properties.IsLeftButtonPressed) return;
            Select(tab);
            // Possibly the start of a drag to reorder the tabs.
            _draggedTab = tab;
            _dragStartPoint = e.GetPosition(TabStrip);
            _isDraggingTab = false;
            e.Pointer.Capture(chip);
        };
        chip.PointerMoved += (_, e) => DragTab(tab, e.GetPosition(TabStrip));
        chip.PointerReleased += (_, e) =>
        {
            if (_draggedTab != tab) return;
            e.Pointer.Capture(null);
            EndTabDrag();
        };
        chip.PointerCaptureLost += (_, _) =>
        {
            if (_draggedTab == tab) EndTabDrag();
        };

        void UpdateDirty() => dirtyDot.IsVisible = capture.Annotations.IsDirty;
        capture.Annotations.Changed += (_, _) => UpdateDirty();
        UpdateDirty();
        return chip;
    }

    /// <summary>Moves the dragged tab past a neighbour once the pointer crosses that neighbour's middle.</summary>
    private void DragTab(Tab tab, Point position)
    {
        if (_draggedTab != tab) return;
        if (!_isDraggingTab)
        {
            if (Math.Abs(position.X - _dragStartPoint.X) < 6) return;
            _isDraggingTab = true;
            tab.Chip.Opacity = 0.75;
        }

        // Centers from the current order and chip widths: after a swap, the layout may not
        // have run yet, so Bounds would still describe the old positions.
        var index = _tabs.IndexOf(tab);
        if (index > 0 && position.X < ChipCenter(index - 1))
            _app.Tabs.Move(index, index - 1);
        else if (index < _tabs.Count - 1 && position.X > ChipCenter(index + 1))
            _app.Tabs.Move(index, index + 1);
    }

    private double ChipCenter(int index)
    {
        var x = 0.0;
        for (var i = 0; i < index; i++) x += _tabs[i].Chip.Bounds.Width + _tabs[i].Chip.Margin.Right;
        return x + _tabs[index].Chip.Bounds.Width / 2;
    }

    private void EndTabDrag()
    {
        if (_draggedTab is { } tab) tab.Chip.Opacity = 1;
        _draggedTab = null;
        _isDraggingTab = false;
    }

    private ContextMenu CreateTabMenu(Capture capture)
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var reveal = Item($"Im {AppController.FileManagerName} zeigen", () =>
        {
            if (capture.LastSavedPath is { } path && File.Exists(path)) AppController.RevealInFileManager(path);
        });
        var menu = new ContextMenu
        {
            Items =
            {
                reveal,
                Item("Speicherort öffnen", () => _ = _app.OpenSaveFolderAsync(this)),
                new Separator(),
                Item("Schließen", () => _app.CloseTab(capture)),
                Item("Andere Reiter schließen", () =>
                {
                    foreach (var other in _app.Tabs.Where(c => c != capture).ToList()) _app.CloseTab(other);
                }),
            },
        };
        // "Show in Finder" only makes sense once the screenshot was saved.
        menu.Opening += (_, _) => reveal.IsVisible = capture.LastSavedPath is { } p && File.Exists(p);
        return menu;
    }

    // ------------------------------------------------------------ glass

    private static readonly (string Key, double Strength)[] GlassBrushes =
    [
        ("ScChromeBrush", 0.7),
        ("ScCanvasBrush", 0.5),
        ("ScGroupBrush", 0.45),
        ("ScTabSelectedBrush", 0.35),
    ];

    /// <summary>
    /// Translucent window (vibrancy on macOS, Mica/Acrylic on Windows): the chrome brushes
    /// get more transparent the higher the setting, so the blurred desktop shines through.
    /// </summary>
    private IDisposable? _glassBackground;

    private void ApplyGlass()
    {
        var intensity = Math.Clamp(_app.Settings.GlassIntensity, 0, 100) / 100.0;
        if (intensity <= 0)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            foreach (var (key, _) in GlassBrushes) Resources.Remove(key);
            // Back to the opaque theme color, following light/dark switches.
            _glassBackground?.Dispose();
            _glassBackground = Bind(BackgroundProperty, this.GetResourceObservable("ScChromeBrush"));
            return;
        }

        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur];
        _glassBackground?.Dispose();
        _glassBackground = null;
        Background = Brushes.Transparent;
        foreach (var (key, strength) in GlassBrushes)
        {
            if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var value) != true || value is not SolidColorBrush brush)
                continue;
            var alpha = (byte)Math.Round(255 * (1 - intensity * strength));
            Resources[key] = new SolidColorBrush(Color.FromArgb(alpha, brush.Color.R, brush.Color.G, brush.Color.B));
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.IsVisible = _tabs.Count == 0;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = _selected is { } t
            ? $"Screencap – {t.Capture.SourceDescription ?? t.Capture.CreatedAt.ToString("dd.MM.yyyy HH:mm:ss")}"
            : "Screencap";

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var command = this.CommandModifier();

        if (e.KeyModifiers == command && e.Key == Key.W && _selected is { } current)
        {
            _app.CloseTab(current.Capture);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers == command && e.Key == Key.N)
        {
            _ = _app.CaptureIntoNewTabAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _tabs.Count > 1 && _selected is { } sel)
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            Select(_tabs[(_tabs.IndexOf(sel) + step + _tabs.Count) % _tabs.Count]);
            e.Handled = true;
            return;
        }

        // Text boxes (inline text tool, OCR text) keep their own keys.
        if (e.Source is TextBox) return;
        if (_selected?.Editor.HandleKey(e) == true) e.Handled = true;
    }
}
