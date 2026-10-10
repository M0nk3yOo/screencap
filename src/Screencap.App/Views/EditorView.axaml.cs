using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Screencap.Core.Settings;
using Screencap.Core.Ocr;
using Screencap.App.Controls;
using Screencap.App.Rendering;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;

namespace Screencap.App.Views;

/// <summary>Full editor for one screenshot: toolbar, canvas, OCR panel and status bar. One per tab.</summary>
public partial class EditorView : UserControl
{
    // Group index separates the tools into visual groups in the toolbar.
    private static readonly (EditorTool Tool, string Icon, string Name, Key Key, int Group)[] Tools =
    [
        (EditorTool.Select, "IconSelect", "Auswählen und verschieben", Key.V, 0),
        (EditorTool.Crop, "IconCrop", "Zuschneiden", Key.C, 0),
        (EditorTool.Arrow, "IconArrow", "Pfeil", Key.A, 1),
        (EditorTool.Rectangle, "IconRectangle", "Rechteck", Key.R, 1),
        (EditorTool.Ellipse, "IconEllipse", "Ellipse", Key.O, 1),
        (EditorTool.Highlight, "IconHighlight", "Marker", Key.H, 1),
        (EditorTool.Text, "IconText", "Text", Key.T, 1),
        (EditorTool.Counter, "IconCounter", "Nummer – ziehen für einen Zeiger", Key.N, 1),
        (EditorTool.Pixelate, "IconPixelate", "Verpixeln / Verwischen", Key.B, 2),
        (EditorTool.Spotlight, "IconSpotlight", "Spotlight", Key.S, 2),
        (EditorTool.TextRecognition, "IconOcr", "Text markieren – Bereich aufziehen, Text und QR-Codes werden erkannt", Key.E, 3),
        (EditorTool.ColorPicker, "IconPicker", "Pipette", Key.I, 2),
    ];

    private static readonly uint[] Palette =
        [0xFFFF3B30, 0xFFFF9500, 0xFFFFCC00, 0xFF34C759, 0xFF007AFF, 0xFFAF52DE, 0xFF000000, 0xFFFFFFFF];

    private const int MaxCustomColors = 6;

    private readonly AppController _app;
    private readonly AnnotationCanvas _canvas;
    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = [];
    private readonly List<(Border Swatch, ArgbColor Color)> _swatches = [];
    private bool _updatingOptions;
    private bool _fitToWindow = true;
    private EditorTool _toolBeforeCrop = EditorTool.Arrow;
    private EditorTool _toolBeforePicker = EditorTool.Arrow;

    /// <summary>
    /// The options popup hides once the user goes on editing the image, and comes back when
    /// a tool is chosen, an annotation is clicked or text is edited.
    /// </summary>
    private bool _optionsDismissed;

    /// <summary>True while an annotation is moved or resized; the canvas is pinned meanwhile.</summary>
    private bool _dragging;
    private TextBox? _textEditor;
    /// <summary>Working copy of the text being typed (new, or a clone of the edited one).</summary>
    private TextAnnotation? _textDraft;
    private IDisposable? _statusTimer;
    private string? _transientStatus;
    private PointD? _pointer;
    private Task? _textDetection;

    // Required by the XAML loader for previews; not used at runtime.
    public EditorView() : this(null!, null!) { }

    internal EditorView(AppController app, Capture capture)
    {
        InitializeComponent();
        _app = app;
        Capture = capture;
        if (capture is null)
        {
            _canvas = null!;
            return;
        }

        _canvas = new AnnotationCanvas(capture, app.GetBitmap(capture));
        CanvasHost.Children.Insert(0, _canvas);

        BuildTools();
        BuildSwatches();
        WireOptions();
        WireLayers();
        _canvas.CanvasBackground = ArgbColor.FromUInt(app.Settings.CanvasBackground);
        app.SettingsChanged += OnSettingsChanged;
        DetachedFromVisualTree += (_, _) => app.SettingsChanged -= OnSettingsChanged;
        WireActions();

        _canvas.TextEditRequested += BeginTextEdit;
        _canvas.ColorPicked += OnColorPicked;
        _canvas.DragStarted += PinCanvasForDrag;
        _canvas.ViewOriginShifted += (dx, dy) =>
        {
            // Growing to the left/top moves the image origin; shift the canvas the other way.
            var m = CanvasFrame.Margin;
            CanvasFrame.Margin = new Thickness(m.Left + dx * _canvas.DisplayScale, m.Top + dy * _canvas.DisplayScale, m.Right, m.Bottom);
        };
        _canvas.DragEnded += UnpinCanvas;
        _canvas.TextRegionSelected += region => _ = RecognizeTextAsync(region);
        Scroller.AddHandler(PointerWheelChangedEvent, OnScrollerWheel, RoutingStrategies.Tunnel);
        Scroller.PointerTouchPadGestureMagnify += (_, e) =>
        {
            ZoomAt(_canvas.Zoom * (1 + e.Delta.X), e.GetPosition(_canvas), e.GetPosition(Scroller));
            e.Handled = true;
        };
        LoadToolMemory();
        _canvas.EditingStarted += () =>
        {
            _optionsDismissed = true;
            OptionsBar.IsVisible = false;
        };
        _canvas.AnnotationClicked += () =>
        {
            _optionsDismissed = false;
            UpdateOptions();
        };
        _canvas.SelectionChanged += () =>
        {
            UpdateOptions();
            RebuildLayers();
        };
        _canvas.PointerImagePositionChanged += p =>
        {
            _pointer = p;
            UpdateStatus();
        };
        capture.Annotations.Changed += (_, _) =>
        {
            UpdateHistoryButtons();
            RebuildLayers();
            if (_fitToWindow && !_dragging) Dispatcher.UIThread.Post(ApplyFitZoom);
        };
        Scroller.SizeChanged += (_, _) =>
        {
            if (_fitToWindow) ApplyFitZoom();
        };

        SelectTool(EditorTool.Arrow);
        SelectColor(ArgbColor.FromUInt(_app.Settings.Tools.Color));
        UpdateHistoryButtons();
    }

    internal Capture Capture { get; }

    private static string CommandSymbol => AppController.IsMac ? "⌘" : "Strg+";

    // ------------------------------------------------------------ toolbar construction

    private void BuildTools()
    {
        var lastGroup = 0;
        foreach (var (tool, icon, name, key, group) in Tools)
        {
            if (group != lastGroup && group != 3)
            {
                ToolPanel.Children.Add(new Border { Classes = { "divider" }, Margin = new Thickness(3, 6) });
                lastGroup = group;
            }
            var button = new ToggleButton
            {
                Content = new PathIcon { Data = Icons.Get(icon) },
                Classes = { "flat" },
            };
            ToolTip.SetTip(button, $"{name} ({key})");
            button.Click += (_, _) => SelectTool(tool);
            _toolButtons[tool] = button;
            // Group 3 lives in the recognition group together with "all text" and QR.
            if (group == 3) RecognitionPanel.Children.Insert(0, button);
            else ToolPanel.Children.Add(button);
        }
    }

    private void BuildSwatches()
    {
        SwatchPanel.Children.Clear();
        _swatches.Clear();
        var custom = _app.Settings.CustomColors.Select(ArgbColor.FromUInt).Where(c => !Palette.Contains(c.Value)).ToList();
        foreach (var color in Palette.Select(ArgbColor.FromUInt).Concat(custom))
        {
            var swatch = new Border
            {
                Background = new SolidColorBrush(AnnotationRenderer.ToColor(color)),
                Classes = { "swatch" },
            };
            Control item = swatch;
            if (custom.Contains(color))
            {
                // Own colors can be removed again with a small × that shows on hover.
                var remove = new Button
                {
                    Classes = { "swatchRemove" },
                    Content = new PathIcon { Data = Icons.Get("IconClose"), Width = 6, Height = 6 },
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                ToolTip.SetTip(remove, "Farbe entfernen");
                remove.Click += (_, _) => RemoveCustomColor(color);
                item = new Panel { Classes = { "customSwatch" }, Children = { swatch, remove } };
            }
            ToolTip.SetTip(swatch, color.ToHex());
            swatch.PointerPressed += (_, _) =>
            {
                SelectColor(color);
                if (_textDraft is { } draft)
                {
                    draft.Color = color;
                    UpdateTextEditorAppearance();
                }
                else
                {
                    _canvas.ApplyColorToSelection();
                }
            };
            _swatches.Add((swatch, color));
            SwatchPanel.Children.Add(item);
        }
        SwatchPanel.Children.Add(CreateColorPickerButton());
        // Keep the current color highlighted after rebuilding.
        SelectColor(_canvas.Color);
    }

    private Button CreateColorPickerButton()
    {
        var view = new ColorView
        {
            Color = AnnotationRenderer.ToColor(_canvas.Color),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            Width = 380,
        };
        view.ColorChanged += (_, e) =>
        {
            SelectColor(new ArgbColor(255, e.NewColor.R, e.NewColor.G, e.NewColor.B));
            _canvas.ApplyColorToSelection();
        };

        var flyout = new Flyout { Content = view, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Closed += (_, _) => RememberCustomColor(_canvas.Color);

        var button = new Button
        {
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 2, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            Flyout = flyout,
            // A color wheel hints at "more colors".
            Background = new ConicGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FF3B30"), 0), new GradientStop(Color.Parse("#FFCC00"), 0.17),
                    new GradientStop(Color.Parse("#34C759"), 0.33), new GradientStop(Color.Parse("#00C7BE"), 0.5),
                    new GradientStop(Color.Parse("#007AFF"), 0.67), new GradientStop(Color.Parse("#AF52DE"), 0.83),
                    new GradientStop(Color.Parse("#FF3B30"), 1),
                },
            },
        };
        ToolTip.SetTip(button, "Weitere Farben …");
        return button;
    }

    private void RemoveCustomColor(ArgbColor color)
    {
        _app.Settings.CustomColors.Remove(color.Value);
        _app.SaveSettings();
        BuildSwatches();
    }

    private void RememberCustomColor(ArgbColor color)
    {
        if (Palette.Contains(color.Value)) return;
        var colors = _app.Settings.CustomColors;
        colors.Remove(color.Value);
        colors.Insert(0, color.Value);
        if (colors.Count > MaxCustomColors) colors.RemoveRange(MaxCustomColors, colors.Count - MaxCustomColors);
        _app.SaveSettings();
        BuildSwatches();
    }

    /// <summary>What the options popup is currently about.</summary>
    private enum OptionsContext
    {
        None,
        Line,
        Text,
        Counter,
        Obscure,
    }

    private OptionsContext CurrentOptionsContext
    {
        get
        {
            var subject = _canvas.Selected switch
            {
                ArrowAnnotation or RectangleAnnotation or EllipseAnnotation => EditorTool.Arrow,
                TextAnnotation => EditorTool.Text,
                CounterAnnotation => EditorTool.Counter,
                PixelateAnnotation => EditorTool.Pixelate,
                null => _canvas.Tool,
                _ => EditorTool.Select,
            };
            if (_textDraft is not null) subject = EditorTool.Text;
            return subject switch
            {
                EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Ellipse => OptionsContext.Line,
                EditorTool.Text => OptionsContext.Text,
                EditorTool.Counter => OptionsContext.Counter,
                EditorTool.Pixelate => OptionsContext.Obscure,
                _ => OptionsContext.None,
            };
        }
    }

    /// <summary>The text whose formatting the popup edits: the one being typed, the selected one, or the defaults.</summary>
    private TextAnnotation TextTarget => _textDraft ?? _canvas.Selected as TextAnnotation ?? _canvas.TextDefaults;

    /// <summary>
    /// The options popup: one slider for "how big" (line width, text size, counter size or
    /// obscure strength) plus text formatting and the pixelate/blur style where relevant.
    /// Everything also edits the selected annotation.
    /// </summary>
    private void WireOptions()
    {
        SizeSlider.ValueChanged += (_, _) =>
        {
            if (_updatingOptions) return;
            switch (CurrentOptionsContext)
            {
                case OptionsContext.Obscure:
                    _canvas.ObscureStrength = SizeSlider.Value;
                    break;
                case OptionsContext.Text when _textDraft is { } editing:
                    _canvas.StrokePoints = SizeSlider.Value;
                    editing.FontSize = ToolSizes.FontFor(SizeSlider.Value) * Capture.Scaling;
                    UpdateTextEditorAppearance();
                    break;
                default:
                    _canvas.StrokePoints = SizeSlider.Value;
                    break;
            }
            _canvas.ApplyStyleToSelection();
            UpdateSizeLabel();
            RememberOptions();
        };
        PixelateStyleButton.Click += (_, _) => SetObscureStyle(ObscureStyle.Pixelate);
        BlurStyleButton.Click += (_, _) => SetObscureStyle(ObscureStyle.Blur);

        var fonts = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        FontCombo.ItemsSource = new[] { DefaultFontName }.Concat(fonts).ToList();
        FontCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingOptions || FontCombo.SelectedItem is not string name) return;
            var family = name == DefaultFontName ? null : name;
            ApplyText(t => t.FontFamily = family);
        };
        BoldButton.Click += (_, _) => ApplyText(t => t.Bold = BoldButton.IsChecked == true);
        ItalicButton.Click += (_, _) => ApplyText(t => t.Italic = ItalicButton.IsChecked == true);
        UnderlineButton.Click += (_, _) => ApplyText(t => t.Underline = UnderlineButton.IsChecked == true);
        StrikeButton.Click += (_, _) => ApplyText(t => t.Strikethrough = StrikeButton.IsChecked == true);
        OutlineStyleButton.Click += (_, _) => ApplyText(t => t.Style = TextStyle.Outline);
        PlainStyleButton.Click += (_, _) => ApplyText(t => t.Style = TextStyle.Plain);
        FilledStyleButton.Click += (_, _) => ApplyText(t => t.Style = TextStyle.Filled);
        UpdateOptions();
    }

    private const string DefaultFontName = "Standard";

    /// <summary>Applies a formatting change to the defaults and to the text being edited or selected.</summary>
    private void ApplyText(Action<TextAnnotation> change)
    {
        change(_canvas.TextDefaults);
        if (_textDraft is { } editing)
        {
            change(editing);
            UpdateTextEditorAppearance();
        }
        else
        {
            _canvas.EditSelected(change);
        }
        UpdateOptions();
        RememberOptions();
    }

    private void SetObscureStyle(ObscureStyle style)
    {
        _canvas.ObscureStyle = style;
        _canvas.ApplyStyleToSelection();
        UpdateOptions();
        RememberOptions();
    }

    /// <summary>Shows the popup for the current tool/selection, fills in its values and places it under the tool.</summary>
    private void UpdateOptions()
    {
        var context = CurrentOptionsContext;
        OptionsBar.IsVisible = context != OptionsContext.None && !_optionsDismissed;
        if (!OptionsBar.IsVisible) return;

        var scaling = Capture.Scaling;
        _updatingOptions = true;
        if (context == OptionsContext.Obscure)
        {
            if (_canvas.Selected is PixelateAnnotation selected)
            {
                _canvas.ObscureStyle = selected.Style;
                _canvas.ObscureStrength = selected.Style == ObscureStyle.Blur
                    ? ToolSizes.StrengthForBlurRadius(selected.BlurRadius / scaling)
                    : ToolSizes.StrengthForBlockSize(selected.BlockSize / scaling);
            }
            SizeSlider.Minimum = ToolSizes.MinStrength;
            SizeSlider.Maximum = ToolSizes.MaxStrength;
            SizeSlider.Value = _canvas.ObscureStrength;
            ToolTip.SetTip(SizeSlider, "Stärke – je höher, desto unleserlicher");
        }
        else
        {
            var stroke = context switch
            {
                OptionsContext.Text when _textDraft is not null || _canvas.Selected is TextAnnotation =>
                    ToolSizes.StrokeForFont(TextTarget.FontSize / scaling),
                OptionsContext.Counter when _canvas.Selected is CounterAnnotation c => ToolSizes.StrokeForCounterRadius(c.Radius / scaling),
                OptionsContext.Line when _canvas.Selected is { } shape => shape.StrokeWidth / scaling,
                _ => _canvas.StrokePoints,
            };
            _canvas.StrokePoints = Math.Clamp(stroke, ToolSizes.MinStroke, ToolSizes.MaxStroke);
            SizeSlider.Minimum = ToolSizes.MinStroke;
            SizeSlider.Maximum = ToolSizes.MaxStroke;
            SizeSlider.Value = _canvas.StrokePoints;
            ToolTip.SetTip(SizeSlider, context switch
            {
                OptionsContext.Text => "Schriftgröße",
                OptionsContext.Counter => "Größe der Nummer",
                _ => "Linienstärke",
            });
        }

        var text = TextTarget;
        FontCombo.SelectedItem = text.FontFamily ?? DefaultFontName;
        BoldButton.IsChecked = text.Bold;
        ItalicButton.IsChecked = text.Italic;
        UnderlineButton.IsChecked = text.Underline;
        StrikeButton.IsChecked = text.Strikethrough;
        OutlineStyleButton.IsChecked = text.Style == TextStyle.Outline;
        PlainStyleButton.IsChecked = text.Style == TextStyle.Plain;
        FilledStyleButton.IsChecked = text.Style == TextStyle.Filled;
        _updatingOptions = false;

        ObscureStylePanel.IsVisible = context == OptionsContext.Obscure;
        TextOptionsPanel.IsVisible = context == OptionsContext.Text;
        PixelateStyleButton.IsChecked = _canvas.ObscureStyle == ObscureStyle.Pixelate;
        BlurStyleButton.IsChecked = _canvas.ObscureStyle == ObscureStyle.Blur;
        UpdateSizeLabel();
        Dispatcher.UIThread.Post(PlaceOptionsBar, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the popup right under the button of the tool it belongs to.</summary>
    private void PlaceOptionsBar()
    {
        var tool = CurrentOptionsContext switch
        {
            OptionsContext.Text => EditorTool.Text,
            OptionsContext.Counter => EditorTool.Counter,
            OptionsContext.Obscure => EditorTool.Pixelate,
            _ => _canvas.Selected switch
            {
                RectangleAnnotation => EditorTool.Rectangle,
                EllipseAnnotation => EditorTool.Ellipse,
                ArrowAnnotation => EditorTool.Arrow,
                _ => _canvas.Tool,
            },
        };
        if (!_toolButtons.TryGetValue(tool, out var button) || OptionsBar.Parent is not Visual host) return;
        var x = button.TranslatePoint(new Point(0, 0), host)?.X ?? 10;
        var maxX = Math.Max(10, host.Bounds.Width - OptionsBar.Bounds.Width - 10);
        OptionsBar.Margin = new Thickness(Math.Clamp(x - 6, 10, maxX), 8, 0, 0);
    }

    private void UpdateSizeLabel()
    {
        SizeLabel.Text = CurrentOptionsContext switch
        {
            OptionsContext.Obscure => string.Create(CultureInfo.CurrentCulture, $"Stärke {SizeSlider.Value:0}"),
            OptionsContext.Text => string.Create(CultureInfo.CurrentCulture, $"{ToolSizes.FontFor(SizeSlider.Value):0} pt"),
            OptionsContext.Counter => string.Create(CultureInfo.CurrentCulture, $"Größe {SizeSlider.Value:0.#}"),
            _ => string.Create(CultureInfo.CurrentCulture, $"Linie {SizeSlider.Value:0.#}"),
        };
    }

    // ------------------------------------------------------------ layers

    private void WireLayers()
    {
        LayersButton.Click += (_, _) => ToggleLayers(LayersButton.IsChecked == true);
        LayersCloseButton.Click += (_, _) => ToggleLayers(false);
        LayerUpButton.Click += (_, _) => MoveSelectedLayer(+1);
        LayerDownButton.Click += (_, _) => MoveSelectedLayer(-1);
        LayerDeleteButton.Click += (_, _) => _canvas.DeleteSelected();
    }

    private void ToggleLayers(bool visible)
    {
        LayersPanel.IsVisible = visible;
        LayersButton.IsChecked = visible;
        if (visible) RebuildLayers();
    }

    private void MoveSelectedLayer(int step)
    {
        if (_canvas.Selected is not { } selected) return;
        var items = Capture.Annotations.Items;
        Capture.Annotations.MoveTo(selected, items.ToList().IndexOf(selected) + step);
    }

    /// <summary>One row per annotation, topmost first — like layers in an image editor.</summary>
    private void RebuildLayers()
    {
        if (!LayersPanel.IsVisible) return;
        LayerList.Children.Clear();
        var items = Capture.Annotations.Items;
        LayersEmpty.IsVisible = items.Count == 0;
        LayerUpButton.IsEnabled = LayerDownButton.IsEnabled = LayerDeleteButton.IsEnabled = _canvas.Selected is not null;

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var annotation = items[i];
            var (icon, name) = Describe(annotation);
            var eye = new Button
            {
                Classes = { "flat" },
                Padding = new Thickness(4),
                MinWidth = 26,
                MinHeight = 26,
                Content = new PathIcon { Data = Icons.Get(annotation.Hidden ? "IconEyeOff" : "IconEye"), Width = 14, Height = 14 },
            };
            ToolTip.SetTip(eye, annotation.Hidden ? "Einblenden" : "Ausblenden");
            eye.Click += (_, _) => Capture.Annotations.SetHidden(annotation, !annotation.Hidden);

            var swatch = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(AnnotationRenderer.ToColor(annotation.Color)),
                IsVisible = annotation is not (PixelateAnnotation or SpotlightAnnotation or ImageAnnotation),
            };
            var row = new Border
            {
                Classes = { "layer" },
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    Children =
                    {
                        new PathIcon { Data = Icons.Get(icon), Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 1 },
                        new Panel { Children = { swatch }, Margin = new Thickness(6, 0), [Grid.ColumnProperty] = 2 },
                        new Panel { Children = { eye }, [Grid.ColumnProperty] = 3 },
                    },
                },
            };
            row.Classes.Set("selected", annotation == _canvas.Selected);
            row.Classes.Set("hidden", annotation.Hidden);
            row.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
                SelectTool(EditorTool.Select);
                _canvas.Select(annotation);
            };
            LayerList.Children.Add(row);
        }
    }

    private static (string Icon, string Name) Describe(Annotation annotation) => annotation switch
    {
        ArrowAnnotation => ("IconArrow", "Pfeil"),
        RectangleAnnotation => ("IconRectangle", "Rechteck"),
        EllipseAnnotation => ("IconEllipse", "Ellipse"),
        HighlightAnnotation => ("IconHighlight", "Marker"),
        TextAnnotation t => ("IconText", $"„{(t.Text.Length > 24 ? t.Text[..24] + "…" : t.Text).Replace('\n', ' ')}“"),
        CounterAnnotation c => ("IconCounter", $"Nummer {c.Number}"),
        PixelateAnnotation p => ("IconPixelate", p.Style == ObscureStyle.Blur ? "Verwischt" : "Verpixelt"),
        SpotlightAnnotation => ("IconSpotlight", "Spotlight"),
        ImageAnnotation => ("IconInsert", "Eingefügtes Bild"),
        _ => ("IconSelect", annotation.GetType().Name),
    };

    private void WireActions()
    {
        ToolTip.SetTip(InsertButton, $"Weitere Aufnahme einfügen ({CommandSymbol}I)");
        ToolTip.SetTip(OcrButton, "Gesamten Text erkennen (OCR)");
        ToolTip.SetTip(QrButton, "QR-Code / Barcode lesen");
        ToolTip.SetTip(UndoButton, $"Rückgängig ({CommandSymbol}Z)");
        ToolTip.SetTip(RedoButton, AppController.IsMac ? "Wiederholen (⇧⌘Z)" : "Wiederholen (Strg+Y)");
        ToolTip.SetTip(CopyButton, $"In die Zwischenablage kopieren ({CommandSymbol}C)");
        ToolTip.SetTip(SaveButton, $"Im Standardordner speichern ({CommandSymbol}S)");
        ToolTip.SetTip(SaveAsButton, AppController.IsMac ? "Speichern unter … (⇧⌘S)" : "Speichern unter … (Strg+Umschalt+S)");

        InsertButton.Click += async (_, _) => await InsertCaptureAsync();
        OcrButton.Click += async (_, _) => await RecognizeTextAsync();
        QrButton.Click += async (_, _) => await ReadCodesAsync();
        OcrOpenLinkButton.Click += (_, _) =>
        {
            if (OcrOpenLinkButton.Tag is string link) AppController.OpenLink(link);
        };
        OcrCloseButton.Click += (_, _) => OcrPanel.IsVisible = false;
        OcrCopyButton.Click += async (_, _) =>
        {
            await SetClipboardTextAsync(OcrText.Text ?? "");
            ShowStatus("Text kopiert");
        };
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        CopyButton.Click += async (_, _) => await CopyAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        SaveAsButton.Click += async (_, _) => await SaveAsAsync();
        PinButton.Click += (_, _) => _app.Pin(Capture);
        FitButton.Click += (_, _) =>
        {
            _fitToWindow = true;
            ApplyFitZoom();
        };
        ActualSizeButton.Click += (_, _) => SetZoom(1);
        ZoomInButton.Click += (_, _) => SetZoom(NextZoomStep(+1));
        ZoomOutButton.Click += (_, _) => SetZoom(NextZoomStep(-1));
        ToolTip.SetTip(ZoomInButton, $"Vergrößern ({CommandSymbol}+ oder {CommandSymbol} + Mausrad)");
        ToolTip.SetTip(ZoomOutButton, $"Verkleinern ({CommandSymbol}−)");
        ToolTip.SetTip(FitButton, $"Ins Fenster einpassen ({CommandSymbol}0)");
        ToolTip.SetTip(ActualSizeButton, $"Originalgröße ({CommandSymbol}1)");
    }

    // ------------------------------------------------------------ tools & colors

    private void SelectTool(EditorTool tool)
    {
        CommitTextEdit();
        if (tool == EditorTool.Crop && _canvas.Tool != EditorTool.Crop) _toolBeforeCrop = _canvas.Tool;
        if (tool == EditorTool.ColorPicker && _canvas.Tool != EditorTool.ColorPicker) _toolBeforePicker = _canvas.Tool;
        _optionsDismissed = false;
        _canvas.Tool = tool;
        _canvas.StrokePoints = RememberedStroke(tool);
        // The color picker keeps the selection, so a picked color can recolor it.
        if (tool is not (EditorTool.Select or EditorTool.ColorPicker)) _canvas.Select(null);
        foreach (var (t, button) in _toolButtons) button.IsChecked = t == tool;
        _canvas.ToolCursor = new Cursor(tool switch
        {
            EditorTool.Select => StandardCursorType.Arrow,
            EditorTool.Text => StandardCursorType.Ibeam,
            _ => StandardCursorType.Cross,
        });
        if (_fitToWindow) Dispatcher.UIThread.Post(ApplyFitZoom);
        if (tool == EditorTool.Pixelate) _textDetection ??= DetectTextRegionsAsync();
        UpdateOptions();
        UpdateStatus();
    }

    private void OnSettingsChanged()
    {
        _canvas.CanvasBackground = ArgbColor.FromUInt(_app.Settings.CanvasBackground);
        _canvas.InvalidateVisual();
    }

    /// <summary>Runs OCR once in the background so the pixelate tool can blur text with one click.</summary>
    private async Task DetectTextRegionsAsync()
    {
        ShowStatus("Textflächen werden erkannt …");
        try
        {
            _canvas.TextRegions = await _app.RecognizeLinesAsync(Capture, visibleOnly: false);
            ShowStatus(_canvas.TextRegions.Count > 0
                ? $"{_canvas.TextRegions.Sum(l => l.Words.Count)} Textflächen erkannt – anklicken zum Verpixeln"
                : "Keine Textflächen erkannt – Bereich aufziehen zum Verpixeln");
        }
        catch (Exception e)
        {
            CrashLog.Write("Text detection", e);
            ShowStatus("Textflächen konnten nicht erkannt werden – Bereich aufziehen zum Verpixeln");
        }
    }

    private void SelectColor(ArgbColor color)
    {
        _canvas.Color = color;
        foreach (var (swatch, c) in _swatches) swatch.Classes.Set("selected", c == color);
        if (Memory.Color != color.Value)
        {
            Memory.Color = color.Value;
            _app.SaveSettingsSoon();
        }
    }

    /// <summary>
    /// A color picked from the screenshot becomes the drawing color (and recolors the
    /// selection); the hex value goes to the clipboard, then the previous tool returns.
    /// </summary>
    private async void OnColorPicked(ArgbColor color)
    {
        var picked = color.WithAlpha(255);
        SelectColor(picked);
        _canvas.ApplyColorToSelection();
        if (!_swatches.Any(s => s.Color == picked)) RememberCustomColor(picked);
        SelectTool(_toolBeforePicker);

        var hex = picked.ToHex();
        await SetClipboardTextAsync(hex);
        ShowStatus($"Farbe {hex} übernommen und kopiert");
    }

    private async Task SetClipboardTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    // ------------------------------------------------------------ zoom

    private void ApplyFitZoom()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var available = Scroller.Bounds.Size;
        if (available.Width <= 0 || available.Height <= 0) return;
        var view = _canvas.ViewRect;
        var width = view.Width / scaling;
        var height = view.Height / scaling;
        const double margin = 64;
        var fit = Math.Min((available.Width - margin) / width, (available.Height - margin) / height);
        // Never enlarge beyond 100 %; small screenshots stay pixel-exact.
        _canvas.Zoom = Math.Clamp(fit, 0.05, 1);
        UpdateStatus();
    }

    private void SetZoom(double zoom)
    {
        CommitTextEdit();
        _fitToWindow = false;
        _canvas.Zoom = Math.Clamp(zoom, 0.05, 16);
        UpdateStatus();
    }

    private static readonly double[] ZoomSteps = [0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8, 12, 16];

    private double NextZoomStep(int direction)
    {
        var current = _canvas.Zoom;
        return direction > 0
            ? ZoomSteps.FirstOrDefault(z => z > current * 1.01, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < current / 1.01, ZoomSteps[0]);
    }

    /// <summary>⌘/Ctrl + mouse wheel zooms towards the pointer; plain wheel keeps scrolling.</summary>
    private void OnScrollerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(this.CommandModifier())) return;
        ZoomAt(_canvas.Zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(_canvas), e.GetPosition(Scroller));
        e.Handled = true;
    }

    /// <summary>Zooms so the image point under <paramref name="pointerOnCanvas"/> stays under the pointer.</summary>
    private void ZoomAt(double zoom, Point pointerOnCanvas, Point pointerOnScroller)
    {
        var old = _canvas.Zoom;
        SetZoom(zoom);
        var factor = _canvas.Zoom / old;
        Dispatcher.UIThread.Post(() =>
        {
            var target = new Point(pointerOnCanvas.X * factor, pointerOnCanvas.Y * factor);
            if (_canvas.TranslatePoint(target, Scroller) is not { } now) return;
            Scroller.Offset = new Vector(
                Math.Max(0, Scroller.Offset.X + now.X - pointerOnScroller.X),
                Math.Max(0, Scroller.Offset.Y + now.Y - pointerOnScroller.Y));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Pins the canvas at its current place so it can grow during a drag without recentering.</summary>
    private void PinCanvasForDrag()
    {
        _dragging = true;
        var b = CanvasFrame.Bounds;
        CanvasFrame.HorizontalAlignment = HorizontalAlignment.Left;
        CanvasFrame.VerticalAlignment = VerticalAlignment.Top;
        CanvasFrame.Margin = new Thickness(b.X, b.Y, 32, 32);
    }

    private void UnpinCanvas()
    {
        _dragging = false;
        CanvasFrame.HorizontalAlignment = HorizontalAlignment.Center;
        CanvasFrame.VerticalAlignment = VerticalAlignment.Center;
        CanvasFrame.Margin = new Thickness(32);
        if (_fitToWindow) Dispatcher.UIThread.Post(ApplyFitZoom);
    }

    // ------------------------------------------------------------ tool memory

    private ToolMemory Memory => _app.Settings.Tools;

    /// <summary>Starts every tool the way it was left last time (also across restarts).</summary>
    private void LoadToolMemory()
    {
        var m = Memory;
        var defaults = _canvas.TextDefaults;
        defaults.FontFamily = m.FontFamily;
        defaults.Bold = m.Bold;
        defaults.Italic = m.Italic;
        defaults.Underline = m.Underline;
        defaults.Strikethrough = m.Strikethrough;
        defaults.Style = m.TextStyle;
        _canvas.ObscureStyle = m.ObscureStyle;
        _canvas.ObscureStrength = m.ObscureStrength;
        _canvas.Color = ArgbColor.FromUInt(m.Color);
    }

    private double RememberedStroke(EditorTool tool) => tool switch
    {
        EditorTool.Text => Memory.TextSize,
        EditorTool.Counter => Memory.CounterSize,
        _ => Memory.StrokeByTool.TryGetValue(tool.ToString(), out var v) ? v : ToolSizes.DefaultStroke,
    };

    /// <summary>Stores the current options of the tool (or selected annotation type).</summary>
    private void RememberOptions()
    {
        var m = Memory;
        var tool = _canvas.Selected switch
        {
            ArrowAnnotation => EditorTool.Arrow,
            RectangleAnnotation => EditorTool.Rectangle,
            EllipseAnnotation => EditorTool.Ellipse,
            TextAnnotation => EditorTool.Text,
            CounterAnnotation => EditorTool.Counter,
            _ => _textDraft is not null ? EditorTool.Text : _canvas.Tool,
        };
        switch (tool)
        {
            case EditorTool.Text:
                m.TextSize = _canvas.StrokePoints;
                break;
            case EditorTool.Counter:
                m.CounterSize = _canvas.StrokePoints;
                break;
            case EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Ellipse:
                m.StrokeByTool[tool.ToString()] = _canvas.StrokePoints;
                break;
        }
        var t = _canvas.TextDefaults;
        m.FontFamily = t.FontFamily;
        m.Bold = t.Bold;
        m.Italic = t.Italic;
        m.Underline = t.Underline;
        m.Strikethrough = t.Strikethrough;
        m.TextStyle = t.Style;
        m.ObscureStyle = _canvas.ObscureStyle;
        m.ObscureStrength = _canvas.ObscureStrength;
        m.Color = _canvas.Color.Value;
        _app.SaveSettingsSoon();
    }

    // ------------------------------------------------------------ text editing

    private void BeginTextEdit(PointD position, TextAnnotation? existing)
    {
        CommitTextEdit();
        var draft = existing is not null ? (TextAnnotation)existing.Clone() : CreateTextFromDefaults(position);

        var editor = new TextBox
        {
            Text = draft.Text,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 60,
            MinHeight = 0,
            AcceptsReturn = true,
            Tag = existing,
        };
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) =>
        {
            // Clicking the formatting controls must not end the edit.
            if (OptionsBar.IsKeyboardFocusWithin || OptionsBar.IsPointerOver) return;
            CommitTextEdit();
        };

        _textEditor = editor;
        _textDraft = draft;
        _optionsDismissed = false;
        _canvas.EditingText = existing;
        TextLayer.Children.Add(editor);
        UpdateTextEditorAppearance();
        _canvas.InvalidateVisual();
        UpdateOptions();
        Dispatcher.UIThread.Post(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    private TextAnnotation CreateTextFromDefaults(PointD position)
    {
        var defaults = _canvas.TextDefaults;
        return new TextAnnotation
        {
            Position = position,
            Color = _canvas.Color,
            FontSize = ToolSizes.FontFor(_canvas.StrokePoints) * Capture.Scaling,
            FontFamily = defaults.FontFamily,
            Bold = defaults.Bold,
            Italic = defaults.Italic,
            Underline = defaults.Underline,
            Strikethrough = defaults.Strikethrough,
            Style = defaults.Style,
        };
    }

    /// <summary>Makes the inline text box look like the final text (font, size, style, box width).</summary>
    private void UpdateTextEditorAppearance()
    {
        if (_textEditor is not { } editor || _textDraft is not { } draft) return;
        var scale = _canvas.DisplayScale;
        editor.FontFamily = string.IsNullOrEmpty(draft.FontFamily) ? FontFamily.Default : new FontFamily(draft.FontFamily);
        editor.FontSize = draft.FontSize * scale;
        editor.FontWeight = draft.Bold ? FontWeight.Bold : FontWeight.Normal;
        editor.FontStyle = draft.Italic ? FontStyle.Italic : FontStyle.Normal;
        // (TextBox has no text decorations; underline/strikethrough show once the text is committed.)
        editor.Foreground = new SolidColorBrush(AnnotationRenderer.TextForeground(draft));
        editor.Background = draft.Style == TextStyle.Filled
            ? new SolidColorBrush(AnnotationRenderer.ToColor(draft.Color))
            : new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));
        editor.Padding = new Thickness(draft.Padding * scale);
        editor.TextWrapping = draft.BoxWidth is null ? TextWrapping.NoWrap : TextWrapping.Wrap;
        editor.Width = draft.BoxWidth is { } width ? (width + 2 * draft.Padding) * scale + 2 : double.NaN;
        var origin = _canvas.ImageToCanvas(draft.Position);
        Canvas.SetLeft(editor, origin.X - 1);
        Canvas.SetTop(editor, origin.Y - 1);
    }

    private void CommitTextEdit()
    {
        if (_textEditor is not { } editor || _textDraft is not { } draft) return;
        _textEditor = null;
        _textDraft = null;
        draft.Text = editor.Text?.TrimEnd() ?? "";
        var annotations = Capture.Annotations;
        TextAnnotation? result = null;

        if (editor.Tag is TextAnnotation existing)
        {
            if (draft.Text.Length == 0)
            {
                annotations.Remove(existing);
            }
            else
            {
                if (!SameText(existing, draft))
                {
                    annotations.BeginChange();
                    CopyText(draft, existing);
                    annotations.NotifyChanged();
                }
                result = existing;
            }
        }
        else if (draft.Text.Length > 0)
        {
            annotations.Add(draft);
            result = draft;
        }

        CloseTextEditor(editor);
        if (result is not null) _canvas.Select(result);
        UpdateOptions();
    }

    private static bool SameText(TextAnnotation a, TextAnnotation b) =>
        a.Text == b.Text && a.FontSize == b.FontSize && a.FontFamily == b.FontFamily && a.Bold == b.Bold &&
        a.Italic == b.Italic && a.Underline == b.Underline && a.Strikethrough == b.Strikethrough &&
        a.Style == b.Style && a.BoxWidth == b.BoxWidth && a.Color == b.Color;

    private static void CopyText(TextAnnotation from, TextAnnotation to)
    {
        to.Text = from.Text;
        to.FontSize = from.FontSize;
        to.FontFamily = from.FontFamily;
        to.Bold = from.Bold;
        to.Italic = from.Italic;
        to.Underline = from.Underline;
        to.Strikethrough = from.Strikethrough;
        to.Style = from.Style;
        to.BoxWidth = from.BoxWidth;
        to.Color = from.Color;
    }

    private void CancelTextEdit()
    {
        if (_textEditor is not { } editor) return;
        _textEditor = null;
        _textDraft = null;
        CloseTextEditor(editor);
        UpdateOptions();
    }

    private void CloseTextEditor(TextBox editor)
    {
        _canvas.EditingText = null;
        TextLayer.Children.Remove(editor);
        _canvas.InvalidateVisual();
        _canvas.Focus();
    }

    // ------------------------------------------------------------ actions

    private async Task InsertCaptureAsync()
    {
        CommitTextEdit();
        if (await _app.CaptureForInsertAsync() is not { } inserted) return;

        // Same physical size as on screen, shrunk if it would not fit the visible area.
        var view = Capture.VisibleRect;
        var factor = Capture.Scaling / inserted.Scaling;
        var width = inserted.Image.Width * factor;
        var height = inserted.Image.Height * factor;
        var fit = Math.Min(1, Math.Min(view.Width * 0.8 / width, view.Height * 0.8 / height));
        width *= fit;
        height *= fit;

        var annotation = new ImageAnnotation
        {
            Image = inserted.Image,
            Rect = new RectD(view.X + (view.Width - width) / 2, view.Y + (view.Height - height) / 2, width, height),
            StrokeWidth = 0,
        };
        Capture.Annotations.Add(annotation);
        SelectTool(EditorTool.Select);
        _canvas.Select(annotation);
        ShowStatus("Aufnahme eingefügt – zum Verschieben ziehen");
    }

    /// <summary>
    /// Recognizes the text of the whole visible image or of a marked region and shows it in
    /// the side box. A marked region is also checked for QR codes, whose content comes first.
    /// </summary>
    private async Task RecognizeTextAsync(IntRect? region = null)
    {
        CommitTextEdit();
        ShowResultPanel("Erkannter Text", region is null ? "Text wird erkannt …" : "Text im markierten Bereich wird erkannt …");
        OcrButton.IsEnabled = false;
        try
        {
            var textTask = _app.RecognizeTextAsync(Capture, region);
            var codesTask = region is null ? Task.FromResult<IReadOnlyList<DecodedCode>>([]) : _app.ReadCodesAsync(Capture, region);
            var text = await textTask;
            var codes = await codesTask;
            if (codes.Count > 0)
            {
                await ShowCodesAsync(codes, text);
                return;
            }
            OcrText.Text = text;
            if (text.Length == 0)
            {
                OcrStatus.Text = "Kein Text gefunden.";
            }
            else
            {
                await SetClipboardTextAsync(text);
                var lines = text.Count(c => c == '\n') + 1;
                OcrStatus.Text = $"{lines} {(lines == 1 ? "Zeile" : "Zeilen")} erkannt und in die Zwischenablage kopiert.";
            }
        }
        catch (Exception e)
        {
            CrashLog.Write("OCR", e);
            OcrStatus.Text = $"Texterkennung fehlgeschlagen: {e.Message}";
        }
        finally
        {
            OcrButton.IsEnabled = true;
        }
    }

    private async Task ReadCodesAsync()
    {
        CommitTextEdit();
        ShowResultPanel("QR-Code", "QR-Codes werden gesucht …");
        QrButton.IsEnabled = false;
        try
        {
            var codes = await _app.ReadCodesAsync(Capture);
            if (codes.Count == 0)
            {
                OcrStatus.Text = "Kein QR-Code oder Barcode gefunden.";
                return;
            }
            await ShowCodesAsync(codes, null);
        }
        catch (Exception e)
        {
            CrashLog.Write("QR", e);
            OcrStatus.Text = $"QR-Code konnte nicht gelesen werden: {e.Message}";
        }
        finally
        {
            QrButton.IsEnabled = true;
        }
    }

    private void ShowResultPanel(string title, string status)
    {
        OcrPanel.IsVisible = true;
        OcrTitle.Text = title;
        OcrText.Text = "";
        OcrStatus.Text = status;
        OcrOpenLinkButton.IsVisible = false;
    }

    /// <summary>Shows decoded codes (plus any recognized text), copies them and offers to open a link.</summary>
    private async Task ShowCodesAsync(IReadOnlyList<DecodedCode> codes, string? text)
    {
        OcrTitle.Text = codes.Count == 1 ? codes[0].Format : $"{codes.Count} Codes";
        var content = string.Join("\n\n", codes.Select(c => c.Text));
        OcrText.Text = string.IsNullOrEmpty(text) ? content : $"{content}\n\n{text}";
        await SetClipboardTextAsync(content);
        OcrStatus.Text = codes.Count == 1 ? "Inhalt in die Zwischenablage kopiert." : "Inhalte in die Zwischenablage kopiert.";
        var link = codes.FirstOrDefault(c => c.IsLink);
        OcrOpenLinkButton.IsVisible = link is not null;
        OcrOpenLinkButton.Tag = link?.Text.Trim();
    }

    private void Undo()
    {
        CommitTextEdit();
        Capture.Annotations.Undo();
        _canvas.Select(null);
    }

    private void Redo()
    {
        Capture.Annotations.Redo();
        _canvas.Select(null);
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = Capture.Annotations.CanUndo;
        RedoButton.IsEnabled = Capture.Annotations.CanRedo;
    }

    private async Task CopyAsync()
    {
        CommitTextEdit();
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        await _app.CopyAsync(Capture, topLevel);
        ShowStatus("In die Zwischenablage kopiert");
    }

    private async Task SaveAsync()
    {
        CommitTextEdit();
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        try
        {
            if (await _app.QuickSaveAsync(Capture, topLevel) is { } path) ShowStatus($"Gespeichert: {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Speichern fehlgeschlagen: {e.Message}");
        }
    }

    private async Task SaveAsAsync()
    {
        CommitTextEdit();
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (await _app.SaveAsAsync(Capture, topLevel) is { } path) ShowStatus($"Gespeichert: {path}");
    }

    // ------------------------------------------------------------ keyboard

    /// <summary>Called by the main window for key presses while this tab is active.</summary>
    internal bool HandleKey(KeyEventArgs e)
    {
        // While typing text, the text box gets all keys.
        if (_textEditor is not null) return false;

        var command = this.CommandModifier();
        var mods = e.KeyModifiers;

        if (mods == command)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(); return true;
                case Key.Y: Redo(); return true;
                case Key.C: _ = CopyAsync(); return true;
                case Key.S: _ = SaveAsync(); return true;
                case Key.I: _ = InsertCaptureAsync(); return true;
                case Key.D0: _fitToWindow = true; ApplyFitZoom(); return true;
                case Key.D1: SetZoom(1); return true;
                case Key.OemPlus or Key.Add: SetZoom(NextZoomStep(+1)); return true;
                case Key.OemMinus or Key.Subtract: SetZoom(NextZoomStep(-1)); return true;
            }
            return false;
        }

        if (mods == (command | KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.Z: Redo(); return true;
                case Key.S: _ = SaveAsAsync(); return true;
            }
            return false;
        }

        if (mods != KeyModifiers.None) return false;

        if (_canvas.Tool == EditorTool.Crop)
        {
            switch (e.Key)
            {
                case Key.Delete or Key.Back:
                    Capture.Annotations.SetCrop(null);
                    return true;
                case Key.Enter or Key.Escape:
                    SelectTool(_toolBeforeCrop);
                    return true;
            }
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                _canvas.DeleteSelected();
                return true;
            case Key.Escape:
                if (_canvas.Selected is not null) _canvas.Select(null);
                else SelectTool(EditorTool.Select);
                return true;
            case Key.P:
                _app.Pin(Capture);
                return true;
        }

        foreach (var (tool, _, _, key, _) in Tools)
        {
            if (key != e.Key) continue;
            SelectTool(tool);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------ status

    private void ShowStatus(string text)
    {
        _transientStatus = text;
        UpdateStatus();
        _statusTimer?.Dispose();
        _statusTimer = DispatcherTimer.RunOnce(() =>
        {
            _transientStatus = null;
            UpdateStatus();
        }, TimeSpan.FromSeconds(3));
    }

    private void UpdateStatus()
    {
        var image = Capture.Image;
        var visible = Capture.VisibleRect;
        ZoomText.Text = string.Create(CultureInfo.CurrentCulture, $"{_canvas.Zoom * 100:0} %");

        if (_transientStatus is not null)
        {
            StatusText.Text = _transientStatus;
            return;
        }
        if (_canvas.Tool == EditorTool.Crop)
        {
            StatusText.Text = "Rahmen an Kanten und Ecken ziehen, verschieben oder neu aufziehen   ·   ⌫ setzt zurück   ·   Enter beendet";
            return;
        }
        if (_canvas.Tool == EditorTool.Pixelate && _canvas.TextRegions is { Count: > 0 })
        {
            StatusText.Text = AppController.IsMac
                ? "Wort anklicken zum Verpixeln   ·   ⌥ + Klick: ganze Zeile   ·   oder Bereich aufziehen"
                : "Wort anklicken zum Verpixeln   ·   Alt + Klick: ganze Zeile   ·   oder Bereich aufziehen";
            return;
        }

        var text = $"{visible.Width} × {visible.Height} px";
        if (_pointer is { } p && p.X >= 0 && p.Y >= 0 && p.X < image.Width && p.Y < image.Height)
        {
            var x = (int)p.X;
            var y = (int)p.Y;
            text += $"   ·   {x - visible.X}, {y - visible.Y}   {image.GetPixel(x, y).ToHex()}";
        }
        StatusText.Text = text;
    }
}
