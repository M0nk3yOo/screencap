using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Screencap.App.Services;

internal static class Shortcuts
{
    /// <summary>⌘ on macOS, Ctrl elsewhere.</summary>
    public static KeyModifiers CommandModifier(this Visual visual) =>
        visual.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
}
