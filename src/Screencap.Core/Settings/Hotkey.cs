using System.Text;

namespace Screencap.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    /// <summary>⌘ on macOS, the Windows key on Windows.</summary>
    Meta = 8,
}

/// <summary>Keys that can be bound globally. Mapped to native key codes per platform.</summary>
public enum HotkeyKey
{
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    PrintScreen,
}

public sealed record Hotkey(HotkeyModifiers Modifiers, HotkeyKey Key)
{
    public string ToDisplayString(bool macStyle)
    {
        var sb = new StringBuilder();
        if (macStyle)
        {
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append('⌃');
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append('⌥');
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append('⇧');
            if (Modifiers.HasFlag(HotkeyModifiers.Meta)) sb.Append('⌘');
            return sb.Append(KeyName).ToString();
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Strg+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Umschalt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) sb.Append("Win+");
        return sb.Append(KeyName).ToString();
    }

    private string KeyName => Key switch
    {
        >= HotkeyKey.D0 and <= HotkeyKey.D9 => ((int)(Key - HotkeyKey.D0)).ToString(),
        HotkeyKey.PrintScreen => "Druck",
        _ => Key.ToString(),
    };
}
