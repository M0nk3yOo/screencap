using Avalonia.Input;
using Screencap.Core.Settings;

namespace Screencap.App.Services;

/// <summary>Converts between Avalonia keys and the platform-neutral hotkey model.</summary>
internal static class HotkeyMapping
{
    public static HotkeyKey? FromKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => HotkeyKey.D0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => HotkeyKey.D0 + (key - Key.NumPad0),
        >= Key.A and <= Key.Z => HotkeyKey.A + (key - Key.A),
        >= Key.F1 and <= Key.F12 => HotkeyKey.F1 + (key - Key.F1),
        Key.PrintScreen => HotkeyKey.PrintScreen,
        _ => null,
    };

    public static Key ToKey(HotkeyKey key) => key switch
    {
        >= HotkeyKey.D0 and <= HotkeyKey.D9 => Key.D0 + (key - HotkeyKey.D0),
        >= HotkeyKey.A and <= HotkeyKey.Z => Key.A + (key - HotkeyKey.A),
        >= HotkeyKey.F1 and <= HotkeyKey.F12 => Key.F1 + (key - HotkeyKey.F1),
        HotkeyKey.PrintScreen => Key.PrintScreen,
        _ => Key.None,
    };

    public static HotkeyModifiers FromKeyModifiers(KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= HotkeyModifiers.Meta;
        return result;
    }

    public static KeyModifiers ToKeyModifiers(HotkeyModifiers modifiers)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= KeyModifiers.Control;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= KeyModifiers.Shift;
        if (modifiers.HasFlag(HotkeyModifiers.Meta)) result |= KeyModifiers.Meta;
        return result;
    }
}
