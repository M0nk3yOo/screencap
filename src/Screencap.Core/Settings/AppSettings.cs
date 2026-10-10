namespace Screencap.Core.Settings;

public enum ImageFormat
{
    Png,
    Jpeg,
}

public sealed class AppSettings
{
    /// <summary>Reopen the editor tabs of the last session on startup.</summary>
    public bool RestoreTabsOnStartup { get; set; }

    /// <summary>Put every new screenshot on the clipboard right away.</summary>
    public bool CopyToClipboardAfterCapture { get; set; }

    /// <summary>Colors picked with the color picker, newest first (ARGB).</summary>
    public List<uint> CustomColors { get; set; } = [];

    /// <summary>Default folder for saving; empty = not chosen yet (the user is asked on first save).</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>Save every new screenshot to <see cref="SaveFolder"/> right after capturing.</summary>
    public bool AutoSaveEveryCapture { get; set; }

    /// <summary>Delete screenshots saved by Screencap after <see cref="DeleteAfterDays"/> days.</summary>
    public bool DeleteOldScreenshots { get; set; } = true;

    public int DeleteAfterDays { get; set; } = 180;

    /// <summary>
    /// Color of canvas area outside the screenshot (grown by inserted images), ARGB.
    /// Alpha 0 = transparent, which PNG keeps; JPG and Windows' DIB clipboard get white.
    /// </summary>
    public uint CanvasBackground { get; set; }

    /// <summary>Format for quick save and auto-save.</summary>
    public ImageFormat SaveFormat { get; set; } = ImageFormat.Png;

    /// <summary>JPEG quality 1–100.</summary>
    public int JpegQuality { get; set; } = 90;

    public Hotkey RegionHotkey { get; set; } = DefaultRegionHotkey();

    public Hotkey FullscreenHotkey { get; set; } = DefaultFullscreenHotkey();

    // Same digits as Shottr; ⌘⇧3/4/5 stay with macOS, on Windows Ctrl replaces ⌘.
    private static HotkeyModifiers PrimaryModifiers =>
        (OperatingSystem.IsMacOS() ? HotkeyModifiers.Meta : HotkeyModifiers.Control) | HotkeyModifiers.Shift;

    public static Hotkey DefaultRegionHotkey() => new(PrimaryModifiers, HotkeyKey.D2);

    public static Hotkey DefaultFullscreenHotkey() => new(PrimaryModifiers, HotkeyKey.D1);

    /// <summary>Suggested location when the user picks a folder for the first time.</summary>
    public static string SuggestedSaveFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screencap");
}
