using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Screencap.App.Rendering;
using Screencap.App.Views;
using Screencap.Core;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Ocr;
using Screencap.Core.Platform;
using Screencap.Core.Session;
using Screencap.Core.Settings;

namespace Screencap.App.Services;

/// <summary>
/// Owns the app-wide state and the capture → preview → edit/pin flow.
/// Everything here runs on the UI thread.
/// </summary>
internal sealed class AppController
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IPlatformServices _platform;
    private readonly SettingsStore _settingsStore;
    private readonly SessionStore _sessionStore;
    private readonly ConditionalWeakTable<Capture, Bitmap> _bitmaps = new();
    private readonly DispatcherTimer _sessionSaveTimer;
    private readonly Window _screenProbe = new();

    private TrayIcon? _trayIcon;
    private PreviewWindow? _preview;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private readonly List<PinWindow> _pins = [];
    private DispatcherTimer? _cleanupTimer;
    private bool _capturing;

    /// <param name="dataDirectory">Where settings and session live; tests pass a temp folder.</param>
    public AppController(IClassicDesktopStyleApplicationLifetime desktop, IPlatformServices platform,
        string? dataDirectory = null)
    {
        _desktop = desktop;
        _platform = platform;
        dataDirectory ??= AppPaths.DataDirectory;
        _settingsStore = new SettingsStore(Path.Combine(dataDirectory, AppPaths.SettingsFileName));
        _sessionStore = new SessionStore(Path.Combine(dataDirectory, AppPaths.SessionDirectoryName));
        Settings = _settingsStore.Load();
        _sessionSaveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => SaveSession());
        Tabs.CollectionChanged += (_, e) =>
        {
            foreach (Capture c in e.NewItems ?? Array.Empty<Capture>()) c.Annotations.Changed += OnTabContentChanged;
            foreach (Capture c in e.OldItems ?? Array.Empty<Capture>()) c.Annotations.Changed -= OnTabContentChanged;
            ScheduleSessionSave();
        };
    }

    public AppSettings Settings { get; }

    /// <summary>Screenshots open in the main window, in tab order.</summary>
    public ObservableCollection<Capture> Tabs { get; } = [];

    public IWindowPlatform WindowPlatform => _platform.Windows;

    public static bool IsMac => OperatingSystem.IsMacOS();

    public void Start()
    {
        if (Settings.RestoreTabsOnStartup)
        {
            try
            {
                foreach (var capture in _sessionStore.Load()) Tabs.Add(capture);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Session restore failed: {e}");
            }
        }

        ApplyTheme();
        _trayIcon = TrayMenu.Create(this);
        RegisterHotkeys();
        ApplyAutostartDefault();

        CleanUpOldScreenshots();
        _cleanupTimer = new DispatcherTimer(TimeSpan.FromHours(6), DispatcherPriority.Background, (_, _) => CleanUpOldScreenshots());
        _cleanupTimer.Start();
    }

    public void Shutdown()
    {
        _sessionSaveTimer.Stop();
        _cleanupTimer?.Stop();
        if (_settingsSaveTimer?.IsEnabled == true) _settingsStore.Save(Settings);
        SaveSession();
        _platform.Hotkeys.Dispose();
        _trayIcon?.Dispose();
    }

    public void Quit() => _desktop.Shutdown();

    public void SaveSettings()
    {
        _settingsStore.Save(Settings);
        ScheduleSessionSave();
        SettingsChanged?.Invoke();
    }

    /// <summary>Raised after any setting changed, so open editors can refresh (e.g. canvas background).</summary>
    public event Action? SettingsChanged;

    private DispatcherTimer? _settingsSaveTimer;

    /// <summary>Saves shortly after the last change; for sliders that change many times per second.</summary>
    public void SaveSettingsSoon()
    {
        _settingsSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
        {
            _settingsSaveTimer!.Stop();
            _settingsStore.Save(Settings);
        });
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    /// <summary>Called when cleanup settings change, so the new limit applies right away.</summary>
    public void ApplyCleanupSettings() => CleanUpOldScreenshots();

    /// <summary>Hotkeys that could not be registered (taken by another app).</summary>
    public IReadOnlyList<Hotkey> FailedHotkeys { get; private set; } = [];

    public void RegisterHotkeys()
    {
        _platform.Hotkeys.UnregisterAll();
        var failed = new List<Hotkey>();
        // Hotkey callbacks come from native code; hop onto the dispatcher so the native
        // handler returns immediately.
        if (!_platform.Hotkeys.TryRegister(Settings.RegionHotkey, () => Dispatcher.UIThread.Post(() => _ = CaptureRegionAsync())))
            failed.Add(Settings.RegionHotkey);
        if (!_platform.Hotkeys.TryRegister(Settings.FullscreenHotkey, () => Dispatcher.UIThread.Post(() => _ = CaptureFullscreenAsync())))
            failed.Add(Settings.FullscreenHotkey);
        if (!_platform.Hotkeys.TryRegister(Settings.OcrHotkey, () => Dispatcher.UIThread.Post(() => _ = CaptureTextAsync())))
            failed.Add(Settings.OcrHotkey);
        if (!_platform.Hotkeys.TryRegister(Settings.ScrollHotkey, () => Dispatcher.UIThread.Post(() => _ = CaptureScrollingAsync())))
            failed.Add(Settings.ScrollHotkey);
        FailedHotkeys = failed;
        if (failed.Count > 0)
            Console.Error.WriteLine($"Hotkeys already in use: {string.Join(", ", failed.Select(h => h.ToDisplayString(IsMac)))}");
    }

    public string FormatHotkey(Hotkey hotkey) => hotkey.ToDisplayString(IsMac);

    // ---------------------------------------------------------------- capture

    /// <summary>Hotkey / tray: select a region, then show the preview next to it.</summary>
    public async Task CaptureRegionAsync()
    {
        if (await SelectRegionAsync() is { } selection)
            await OnCapturedAsync(selection.Capture, selection.Region, selection.Screen);
    }

    /// <summary>Marks a screen region and recognizes its text; the result box copies it right away.</summary>
    public async Task CaptureTextAsync()
    {
        if (await SelectRegionAsync() is not { } selection) return;
        string text;
        IReadOnlyList<DecodedCode> codes;
        try
        {
            // QR codes are read alongside the text: marking a code shows its content.
            var textTask = RecognizeTextAsync(selection.Capture);
            var codesTask = ReadCodesAsync(selection.Capture);
            text = await textTask;
            codes = await codesTask;
        }
        catch (Exception e)
        {
            await MessageDialog.ShowErrorAsync("Texterkennung fehlgeschlagen", e);
            return;
        }
        new TextResultWindow(this, selection.Capture, text, codes, selection.Region, selection.Screen).Show();
    }

    public IAutostart Autostart => _platform.Autostart;

    /// <summary>On the very first launch, Screencap registers itself to start at login.</summary>
    private void ApplyAutostartDefault()
    {
        if (!_platform.Autostart.IsSupported) return;
        try
        {
            // Re-register an existing entry so it points to this copy (the app may have been moved).
            if (_platform.Autostart.IsEnabled) _platform.Autostart.SetEnabled(true);
            if (Settings.AutostartConfigured) return;
            _platform.Autostart.SetEnabled(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Autostart", e);
        }
        Settings.AutostartConfigured = true;
        SaveSettings();
    }

    /// <summary>Light, dark or following the system.</summary>
    public void ApplyTheme()
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = Settings.Theme switch
        {
            AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
            AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Scroll capture: select a region, then the user scrolls while frames are captured and
    /// stitched. The result opens in the editor.
    /// </summary>
    public async Task CaptureScrollingAsync()
    {
        if (await SelectRegionAsync() is not { } selection) return;
        PixelBuffer? stitched;
        try
        {
            stitched = await new ScrollCaptureWindow(_platform, selection.Region, selection.Screen).RunAsync();
        }
        catch (Exception e)
        {
            await MessageDialog.ShowErrorAsync("Scroll-Aufnahme fehlgeschlagen", e);
            return;
        }
        if (stitched is null) return;

        var capture = new Capture(stitched, selection.Capture.Scaling)
        {
            SourceApp = selection.Capture.SourceApp,
            SourceTitle = selection.Capture.SourceTitle,
        };
        LastCapture = capture;
        var window = ShowMainWindow();
        OpenInEditor(capture);
        await AfterCaptureAsync(capture, window);
    }

    /// <summary>Stops the global hotkeys, e.g. while the user records a new one.</summary>
    public void SuspendHotkeys() => _platform.Hotkeys.UnregisterAll();

    /// <summary>Re-registers hotkeys and rebuilds the tray menu after hotkey settings changed.</summary>
    public void ApplyHotkeySettings()
    {
        SaveSettings();
        RegisterHotkeys();
        _trayIcon?.Dispose();
        _trayIcon = TrayMenu.Create(this);
    }

    /// <summary>From the editor: capture straight into a new tab, without preview.</summary>
    public async Task CaptureIntoNewTabAsync()
    {
        if (await WithMainWindowHiddenAsync(SelectRegionAsync) is not { } selection) return;
        var window = ShowMainWindow();
        OpenInEditor(selection.Capture);
        await AfterCaptureAsync(selection.Capture, window);
    }

    /// <summary>From the editor: capture a region to paste into the current screenshot.</summary>
    public async Task<Capture?> CaptureForInsertAsync() =>
        (await WithMainWindowHiddenAsync(SelectRegionAsync))?.Capture;

    private sealed record RegionSelection(Capture Capture, PixelRect Region, Screen Screen);

    /// <summary>Freezes all screens, lets the user drag a region and returns the cropped capture.</summary>
    private async Task<RegionSelection?> SelectRegionAsync()
    {
        if (_capturing) return null;
        _capturing = true;
        var overlays = new List<SelectionOverlayWindow>();
        try
        {
            if (!await EnsurePermissionAsync()) return null;
            _preview?.Close();

            var screens = _screenProbe.Screens.All.ToList();
            var windows = ListWindows();
            var shots = await Task.WhenAll(screens.Select(s => _platform.Capturer.CaptureAsync(ToScreenInfo(s))));

            var selection = new TaskCompletionSource<(int Index, Rect Rect)?>();
            for (var i = 0; i < screens.Count; i++)
            {
                var index = i;
                var overlay = new SelectionOverlayWindow(screens[i], shots[i], shots[i].ToBitmap(), _platform.Windows, windows);
                overlay.Selector.Completed += rect => selection.TrySetResult(rect is { } r ? (index, r) : null);
                overlay.Closed += (_, _) => selection.TrySetResult(null);
                overlays.Add(overlay);
                overlay.Show();
            }

            if (await selection.Task is not var (screenIndex, rect)) return null;

            var screen = screens[screenIndex];
            var pixels = shots[screenIndex];
            var imageScale = overlays[screenIndex].Selector.ImageScale;
            CloseAll(overlays);

            var crop = new IntRect(
                (int)Math.Round(rect.X * imageScale), (int)Math.Round(rect.Y * imageScale),
                Math.Max(1, (int)Math.Round(rect.Width * imageScale)), Math.Max(1, (int)Math.Round(rect.Height * imageScale)));

            // The region in the toolkit's screen coordinates, for placing preview and pin.
            var region = new PixelRect(
                screen.Bounds.X + (int)Math.Round(rect.X * screen.Scaling),
                screen.Bounds.Y + (int)Math.Round(rect.Y * screen.Scaling),
                (int)Math.Round(rect.Width * screen.Scaling),
                (int)Math.Round(rect.Height * screen.Scaling));
            var capture = new Capture(pixels.Crop(crop), imageScale);
            AttachSource(capture, windows, region);
            return new RegionSelection(capture, region, screen);
        }
        catch (Exception e)
        {
            await MessageDialog.ShowErrorAsync("Aufnahme fehlgeschlagen", e);
            return null;
        }
        finally
        {
            CloseAll(overlays);
            _capturing = false;
        }
    }

    /// <summary>Keeps the editor out of the screenshot while capturing from it.</summary>
    private async Task<T> WithMainWindowHiddenAsync<T>(Func<Task<T>> action)
    {
        var window = _mainWindow;
        if (window is null || !window.IsVisible) return await action();
        window.Hide();
        // Let the window server finish the hide animation before the screen is frozen.
        await Task.Delay(OperatingSystem.IsMacOS() ? 300 : 150);
        try
        {
            return await action();
        }
        finally
        {
            window.Show();
            _platform.Windows.ActivateApplication();
            window.Activate();
        }
    }

    /// <summary>Recognizes the text in the visible (cropped) part of the screenshot, or in <paramref name="region"/>.</summary>
    public async Task<string> RecognizeTextAsync(Capture capture, IntRect? region = null)
    {
        if (region is not { } r) return TextLayout.ToPlainText(await RecognizeLinesAsync(capture));
        var area = r.Intersect(new IntRect(0, 0, capture.Image.Width, capture.Image.Height));
        if (area.IsEmpty) return "";
        return TextLayout.ToPlainText(await _platform.TextRecognizer.RecognizeAsync(capture.Image.Crop(area)));
    }

    /// <summary>QR codes and barcodes in the visible image or in <paramref name="region"/>.</summary>
    public Task<IReadOnlyList<DecodedCode>> ReadCodesAsync(Capture capture, IntRect? region = null)
    {
        var full = new IntRect(0, 0, capture.Image.Width, capture.Image.Height);
        var area = (region ?? capture.VisibleRect).Intersect(full);
        if (area.IsEmpty) return Task.FromResult<IReadOnlyList<DecodedCode>>([]);
        var image = area == full ? capture.Image : capture.Image.Crop(area);
        return Task.Run(() => CodeReader.Read(image));
    }

    public static void OpenLink(string url)
    {
        if (IsMac) System.Diagnostics.Process.Start("open", [url]);
        else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>OCR lines with boxes in full-image coordinates, of the visible part or the whole image.</summary>
    public async Task<IReadOnlyList<RecognizedLine>> RecognizeLinesAsync(Capture capture, bool visibleOnly = true)
    {
        var full = new IntRect(0, 0, capture.Image.Width, capture.Image.Height);
        // OCR reads the screenshot itself; area gained around it holds no text of its own.
        var visible = visibleOnly ? capture.VisibleRect.Intersect(full) : full;
        if (visible.IsEmpty) return [];
        var isFull = visible == full;
        var lines = await _platform.TextRecognizer.RecognizeAsync(isFull ? capture.Image : capture.Image.Crop(visible));
        if (isFull) return lines;
        return lines.Select(l => new RecognizedLine(l.Text, l.Box.Offset(visible.X, visible.Y),
            l.Words.Select(w => w with { Box = w.Box.Offset(visible.X, visible.Y) }).ToList())).ToList();
    }

    public async Task CaptureFullscreenAsync()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            if (!await EnsurePermissionAsync()) return;
            _preview?.Close();

            var cursor = _platform.Windows.GetCursorPosition();
            var screens = _screenProbe.Screens;
            var screen = screens.ScreenFromPoint(new PixelPoint((int)cursor.X, (int)cursor.Y))
                         ?? screens.Primary ?? screens.All[0];
            var windows = ListWindows();
            var pixels = await _platform.Capturer.CaptureAsync(ToScreenInfo(screen));
            var imageScale = pixels.Width / (screen.Bounds.Width / screen.Scaling);
            var capture = new Capture(pixels, imageScale);
            AttachSource(capture, windows, screen.Bounds);
            await OnCapturedAsync(capture, screen.Bounds, screen);
        }
        catch (Exception e)
        {
            await MessageDialog.ShowErrorAsync("Aufnahme fehlgeschlagen", e);
        }
        finally
        {
            _capturing = false;
        }
    }

    /// <summary>The most recent capture shown in the preview (for tests and diagnostics).</summary>
    internal Capture? LastCapture { get; private set; }

    private async Task OnCapturedAsync(Capture capture, PixelRect region, Screen screen)
    {
        LastCapture = capture;
        var preview = new PreviewWindow(this, capture, region, screen);
        _preview = preview;
        preview.Closed += (_, _) =>
        {
            if (_preview == preview) _preview = null;
        };
        preview.Show();
        await AfterCaptureAsync(capture, preview, preview.ShowToast);
    }

    private async Task<bool> EnsurePermissionAsync()
    {
        if (_platform.Capturer.HasPermission()) return true;
        var choice = await MessageDialog.ShowAsync("Berechtigung erforderlich",
            "Screencap benötigt die Berechtigung „Bildschirmaufnahme“.\n\n" +
            "Bitte erlaube sie in den Systemeinstellungen unter Datenschutz & Sicherheit " +
            "und starte Screencap danach neu.",
            "Systemeinstellungen öffnen", "Abbrechen");
        if (choice == 0) _platform.Capturer.RequestPermission();
        return false;
    }

    /// <summary>Open windows at capture time (frontmost first); best effort, never blocks a capture.</summary>
    private IReadOnlyList<WindowInfo> ListWindows()
    {
        try
        {
            return _platform.WindowInspector.GetWindows();
        }
        catch (Exception e)
        {
            CrashLog.Write("Window list", e);
            return [];
        }
    }

    /// <summary>Remembers which app/window the screenshot most likely shows (for file metadata).</summary>
    private static void AttachSource(Capture capture, IReadOnlyList<WindowInfo> windows, PixelRect region)
    {
        if (WindowMatcher.Find(windows, new IntRect(region.X, region.Y, region.Width, region.Height)) is not { } window) return;
        capture.SourceApp = window.App;
        capture.SourceTitle = window.Title == window.App ? null : window.Title;
    }

    private static ScreenInfo ToScreenInfo(Screen s) =>
        new(new IntRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling);

    private static void CloseAll(List<SelectionOverlayWindow> overlays)
    {
        foreach (var overlay in overlays)
        {
            var image = overlay.Selector.Image;
            overlay.Close();
            image.Dispose();
        }
        overlays.Clear();
    }

    // ---------------------------------------------------------------- actions

    /// <summary>The unannotated screenshot as a bitmap, created once per capture.</summary>
    public Bitmap GetBitmap(Capture capture) => _bitmaps.GetValue(capture, c => c.Image.ToBitmap());

    public Bitmap Flatten(Capture capture) =>
        CaptureRenderer.Flatten(capture, ArgbColor.FromUInt(Settings.CanvasBackground), GetBitmap(capture));

    private static readonly ArgbColor White = new(255, 255, 255, 255);

    /// <summary>The flattened screenshot encoded as PNG or JPEG.</summary>
    public byte[] Encode(Capture capture, ImageFormat format)
    {
        using var image = Flatten(capture);
        var png = image.ToPng();
        // App, window title and time go into the file (PNG text/XMP, JPEG EXIF/XMP).
        var metadata = new ImageMetadata(capture.SourceApp, capture.SourceTitle, capture.CreatedAt);
        return format == ImageFormat.Jpeg
            ? MetadataWriter.AddToJpeg(PixelBuffer.Decode(png).EncodeJpeg(Settings.JpegQuality, White), metadata)
            : MetadataWriter.AddToPng(png, metadata);
    }

    /// <summary>
    /// Copies the annotated screenshot via the native clipboard. Avalonia's own bitmap
    /// clipboard left an empty PNG entry on macOS, so pasting into other apps failed.
    /// </summary>
    public Task CopyAsync(Capture capture, TopLevel topLevel)
    {
        using var image = Flatten(capture);
        _platform.Clipboard.SetImage(image.ToPixelBuffer());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves into the configured folder without asking, like Shottr's ⌘S. If no folder is set
    /// yet, the user picks one first. Returns null if that was cancelled.
    /// </summary>
    public async Task<string?> QuickSaveAsync(Capture capture, TopLevel topLevel)
    {
        if (await EnsureSaveFolderAsync(topLevel) is not { } folder) return null;
        Directory.CreateDirectory(folder);
        var path = ScreenshotFiles.UniquePath(Path.Combine(folder, capture.FileNameFor(Settings.SaveFormat)));
        await File.WriteAllBytesAsync(path, Encode(capture, Settings.SaveFormat));
        capture.Annotations.MarkSaved();
        capture.LastSavedPath = path;
        return path;
    }

    /// <summary>The configured save folder, asking the user to choose one if none is set.</summary>
    public async Task<string?> EnsureSaveFolderAsync(TopLevel topLevel)
    {
        if (!string.IsNullOrWhiteSpace(Settings.SaveFolder)) return Settings.SaveFolder;

        var suggested = AppSettings.SuggestedSaveFolder();
        Directory.CreateDirectory(suggested);
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Speicherort für Screenshots wählen",
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(suggested),
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return null;
        Settings.SaveFolder = path;
        SaveSettings();
        return path;
    }

    /// <summary>Applies the "after capture" settings: copy and/or save automatically.</summary>
    private async Task AfterCaptureAsync(Capture capture, TopLevel topLevel, Action<string>? notify = null)
    {
        try
        {
            if (Settings.CopyToClipboardAfterCapture) await CopyAsync(capture, topLevel);
            if (Settings.AutoSaveEveryCapture && await QuickSaveAsync(capture, topLevel) is { } path)
                notify?.Invoke($"Gespeichert: {Path.GetFileName(path)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Auto save", e);
            notify?.Invoke("Automatisches Speichern fehlgeschlagen");
        }
    }

    /// <summary>Deletes old screenshots per settings; runs in the background.</summary>
    private void CleanUpOldScreenshots()
    {
        if (!Settings.DeleteOldScreenshots || Settings.DeleteAfterDays <= 0) return;
        var folder = Settings.SaveFolder;
        var maxAge = TimeSpan.FromDays(Settings.DeleteAfterDays);
        Task.Run(() =>
        {
            var deleted = ScreenshotFiles.DeleteOlderThan(folder, maxAge, DateTime.UtcNow);
            if (deleted > 0) Console.WriteLine($"Deleted {deleted} screenshots older than {maxAge.TotalDays} days");
        });
    }

    /// <summary>"Save as" with a choice of PNG or JPEG.</summary>
    public async Task<string?> SaveAsAsync(Capture capture, TopLevel topLevel)
    {
        var startFolder = string.IsNullOrWhiteSpace(Settings.SaveFolder) ? AppSettings.SuggestedSaveFolder() : Settings.SaveFolder;
        Directory.CreateDirectory(startFolder);
        var png = FilePickerFileTypes.ImagePng;
        var jpeg = FilePickerFileTypes.ImageJpg;
        var preferJpeg = Settings.SaveFormat == ImageFormat.Jpeg;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Screenshot speichern",
            SuggestedFileName = Path.GetFileNameWithoutExtension(capture.FileNameFor(Settings.SaveFormat)),
            DefaultExtension = preferJpeg ? "jpg" : "png",
            FileTypeChoices = preferJpeg ? [jpeg, png] : [png, jpeg],
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startFolder),
        });
        if (file is null) return null;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        var format = extension is ".jpg" or ".jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;
        var bytes = Encode(capture, format);
        await using (var stream = await file.OpenWriteAsync())
            await stream.WriteAsync(bytes);
        capture.Annotations.MarkSaved();
        capture.LastSavedPath = file.TryGetLocalPath();
        return capture.LastSavedPath ?? file.Name;
    }

    public IReadOnlyList<PinWindow> Pins => _pins;

    public void Pin(Capture capture, PixelPoint? position = null)
    {
        var pin = new PinWindow(this, capture, Flatten(capture));
        _pins.Add(pin);
        pin.Closed += (_, _) => _pins.Remove(pin);
        if (position is { } p)
        {
            pin.WindowStartupLocation = WindowStartupLocation.Manual;
            pin.Position = p;
        }
        else
        {
            pin.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        pin.Show();
    }

    public void OpenInEditor(Capture capture)
    {
        if (!Tabs.Contains(capture)) Tabs.Add(capture);
        var window = ShowMainWindow();
        window.SelectTab(capture);
    }

    public MainWindow ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += (_, _) =>
            {
                _mainWindow = null;
                _platform.Windows.SetDockIconVisible(false);
                // Without session restore, closing the window discards its tabs.
                if (!Settings.RestoreTabsOnStartup) Tabs.Clear();
            };
            _mainWindow.Show();
            // While the editor is open, Screencap shows up in the Dock and the app switcher.
            _platform.Windows.SetDockIconVisible(true);
        }
        _platform.Windows.ActivateApplication();
        _mainWindow.Activate();
        return _mainWindow;
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _platform.Windows.ActivateApplication();
        _settingsWindow.Activate();
    }

    public void CloseTab(Capture capture) => Tabs.Remove(capture);

    public static string FileManagerName => IsMac ? "Finder" : "Explorer";

    /// <summary>Shows the file selected in Finder / Explorer.</summary>
    public static void RevealInFileManager(string path)
    {
        if (IsMac) System.Diagnostics.Process.Start("open", ["-R", path]);
        else System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    /// <summary>Opens the save folder, asking for one first if none is set.</summary>
    public async Task OpenSaveFolderAsync(TopLevel topLevel)
    {
        if (await EnsureSaveFolderAsync(topLevel) is not { } folder) return;
        Directory.CreateDirectory(folder);
        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    // ---------------------------------------------------------------- session

    private void OnTabContentChanged(object? sender, EventArgs e) => ScheduleSessionSave();

    private void ScheduleSessionSave()
    {
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void SaveSession()
    {
        _sessionSaveTimer.Stop();
        try
        {
            if (Settings.RestoreTabsOnStartup) _sessionStore.Save(Tabs);
            else _sessionStore.Clear();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Session could not be saved: {e.Message}");
        }
    }
}
