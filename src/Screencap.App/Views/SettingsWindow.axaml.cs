using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Screencap.App.Services;
using Screencap.Core.Settings;

namespace Screencap.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;

    // Required by the XAML loader for previews; not used at runtime.
    public SettingsWindow() : this(null!) { }

    internal SettingsWindow(AppController app)
    {
        InitializeComponent();
        Icon = AppIcon.Window;
        _app = app;
        if (app is null) return;

        var settings = app.Settings;
        CopyAfterCapture.IsChecked = settings.CopyToClipboardAfterCapture;
        AutoSave.IsChecked = settings.AutoSaveEveryCapture;
        RestoreTabs.IsChecked = settings.RestoreTabsOnStartup;
        SaveFolder.Text = settings.SaveFolder;
        DeleteOld.IsChecked = settings.DeleteOldScreenshots;
        DeleteDays.Value = settings.DeleteAfterDays;
        DeleteDays.IsEnabled = settings.DeleteOldScreenshots;
        FormatPng.IsChecked = settings.SaveFormat == ImageFormat.Png;
        FormatJpeg.IsChecked = settings.SaveFormat == ImageFormat.Jpeg;
        JpegQuality.Value = settings.JpegQuality;
        UpdateJpegControls();
        LoadBackground(settings.CanvasBackground);
        GlassIntensity.Value = settings.GlassIntensity;
        UpdateGlassText();
        GlassIntensity.ValueChanged += (_, _) =>
        {
            settings.GlassIntensity = (int)Math.Round(GlassIntensity.Value);
            UpdateGlassText();
            app.SaveSettings();
        };

        RegionHotkey.Click += (_, _) => StartRecording(RegionHotkey, h => settings.RegionHotkey = h);
        FullscreenHotkey.Click += (_, _) => StartRecording(FullscreenHotkey, h => settings.FullscreenHotkey = h);
        OcrHotkey.Click += (_, _) => StartRecording(OcrHotkey, h => settings.OcrHotkey = h);
        ResetHotkeys.Click += (_, _) =>
        {
            CancelRecording();
            settings.RegionHotkey = AppSettings.DefaultRegionHotkey();
            settings.FullscreenHotkey = AppSettings.DefaultFullscreenHotkey();
            settings.OcrHotkey = AppSettings.DefaultOcrHotkey();
            app.ApplyHotkeySettings();
            ShowHotkeys();
        };
        AddHandler(KeyDownEvent, OnRecordKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += (_, _) => CancelRecording();
        ShowHotkeys();

        CopyAfterCapture.IsCheckedChanged += (_, _) => Update(s => s.CopyToClipboardAfterCapture = CopyAfterCapture.IsChecked == true);
        RestoreTabs.IsCheckedChanged += (_, _) => Update(s => s.RestoreTabsOnStartup = RestoreTabs.IsChecked == true);
        AutoSave.IsCheckedChanged += async (_, _) =>
        {
            Update(s => s.AutoSaveEveryCapture = AutoSave.IsChecked == true);
            // Auto-save needs a folder; ask right away instead of at the next capture.
            if (AutoSave.IsChecked == true && await app.EnsureSaveFolderAsync(this) is null)
                AutoSave.IsChecked = false;
            SaveFolder.Text = settings.SaveFolder;
        };
        DeleteOld.IsCheckedChanged += (_, _) =>
        {
            DeleteDays.IsEnabled = DeleteOld.IsChecked == true;
            Update(s => s.DeleteOldScreenshots = DeleteOld.IsChecked == true);
            app.ApplyCleanupSettings();
        };
        DeleteDays.ValueChanged += (_, _) =>
        {
            if (DeleteDays.Value is not { } days) return;
            Update(s => s.DeleteAfterDays = (int)days);
            app.ApplyCleanupSettings();
        };
        FormatPng.IsCheckedChanged += (_, _) =>
        {
            Update(s => s.SaveFormat = FormatJpeg.IsChecked == true ? ImageFormat.Jpeg : ImageFormat.Png);
            UpdateJpegControls();
        };
        JpegQuality.ValueChanged += (_, _) =>
        {
            Update(s => s.JpegQuality = (int)Math.Round(JpegQuality.Value));
            UpdateJpegControls();
        };
        BackgroundMode.SelectionChanged += (_, _) => SaveBackground();
        BackgroundColor.ColorChanged += (_, _) =>
        {
            if (BackgroundMode.SelectedIndex == CustomBackgroundIndex) SaveBackground();
        };
        ChooseFolder.Click += async (_, _) => await ChooseFolderAsync();
        OpenFolder.Click += async (_, _) =>
        {
            if (await app.EnsureSaveFolderAsync(this) is not { } folder) return;
            SaveFolder.Text = folder;
            Directory.CreateDirectory(folder);
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        };
    }

    private Button? _recordingButton;
    private Action<Hotkey>? _recordingApply;

    private void UpdateGlassText() =>
        GlassIntensityText.Text = GlassIntensity.Value < 1 ? "aus" : $"{GlassIntensity.Value:0} %";

    private void ShowHotkeys()
    {
        var s = _app.Settings;
        RegionHotkey.Content = _app.FormatHotkey(s.RegionHotkey);
        FullscreenHotkey.Content = _app.FormatHotkey(s.FullscreenHotkey);
        OcrHotkey.Content = _app.FormatHotkey(s.OcrHotkey);
        HotkeyWarning.IsVisible = _app.FailedHotkeys.Count > 0;
        HotkeyWarning.Text = "Bereits von einer anderen App oder dem System belegt: " +
                             string.Join(", ", _app.FailedHotkeys.Select(_app.FormatHotkey));
    }

    /// <summary>The next key combination pressed becomes the new hotkey.</summary>
    private void StartRecording(Button button, Action<Hotkey> apply)
    {
        CancelRecording();
        // Global hotkeys would fire instead of reaching this window.
        _app.SuspendHotkeys();
        _recordingButton = button;
        _recordingApply = apply;
        button.Content = "Tastenkombination drücken …";
        button.Classes.Add("accent");
    }

    private void CancelRecording()
    {
        if (_recordingButton is null) return;
        _recordingButton.Classes.Remove("accent");
        _recordingButton = null;
        _recordingApply = null;
        _app.RegisterHotkeys();
        ShowHotkeys();
    }

    private void OnRecordKey(object? sender, KeyEventArgs e)
    {
        if (_recordingButton is null || _recordingApply is null) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            CancelRecording();
            return;
        }
        if (HotkeyMapping.FromKey(e.Key) is not { } key) return; // modifier alone or unsupported key

        var modifiers = HotkeyMapping.FromKeyModifiers(e.KeyModifiers);
        // Without a modifier only function keys and Print Screen make sense as global hotkeys.
        if (modifiers == HotkeyModifiers.None && key is not (>= HotkeyKey.F1 and <= HotkeyKey.F12 or HotkeyKey.PrintScreen))
            return;

        _recordingApply(new Hotkey(modifiers, key));
        _recordingButton.Classes.Remove("accent");
        _recordingButton = null;
        _recordingApply = null;
        _app.ApplyHotkeySettings();
        ShowHotkeys();
    }

    private const int CustomBackgroundIndex = 3;
    private const uint WhiteArgb = 0xFFFFFFFF;
    private const uint BlackArgb = 0xFF000000;
    private bool _loadingBackground;

    private void UpdateJpegControls()
    {
        JpegQualityRow.IsVisible = FormatJpeg.IsChecked == true;
        JpegQualityText.Text = $"{JpegQuality.Value:0} %";
    }

    private void LoadBackground(uint argb)
    {
        _loadingBackground = true;
        BackgroundMode.SelectedIndex = argb switch
        {
            _ when (argb >> 24) == 0 => 0,
            WhiteArgb => 1,
            BlackArgb => 2,
            _ => CustomBackgroundIndex,
        };
        BackgroundColor.Color = Avalonia.Media.Color.FromUInt32((argb >> 24) == 0 ? 0xFF808080 : argb);
        BackgroundColor.IsVisible = BackgroundMode.SelectedIndex == CustomBackgroundIndex;
        _loadingBackground = false;
    }

    private void SaveBackground()
    {
        if (_loadingBackground) return;
        var color = BackgroundColor.Color;
        uint argb = BackgroundMode.SelectedIndex switch
        {
            1 => WhiteArgb,
            2 => BlackArgb,
            CustomBackgroundIndex => 0xFF000000 | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B,
            _ => 0,
        };
        BackgroundColor.IsVisible = BackgroundMode.SelectedIndex == CustomBackgroundIndex;
        Update(s => s.CanvasBackground = argb);
    }

    private void Update(Action<AppSettings> change)
    {
        change(_app.Settings);
        _app.SaveSettings();
    }

    private async Task ChooseFolderAsync()
    {
        var current = string.IsNullOrWhiteSpace(_app.Settings.SaveFolder)
            ? AppSettings.SuggestedSaveFolder()
            : _app.Settings.SaveFolder;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Speicherort wählen",
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current),
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        SaveFolder.Text = path;
        Update(s => s.SaveFolder = path);
        _app.ApplyCleanupSettings();
    }
}
