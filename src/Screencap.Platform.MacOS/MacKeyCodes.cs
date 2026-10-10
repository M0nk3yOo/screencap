using Screencap.Core.Settings;

namespace Screencap.Platform.MacOS;

/// <summary>Virtual key codes from HIToolbox/Events.h (kVK_ANSI_*), which follow the US layout.</summary>
internal static class MacKeyCodes
{
    private static readonly Dictionary<HotkeyKey, uint> Codes = new()
    {
        [HotkeyKey.A] = 0x00, [HotkeyKey.S] = 0x01, [HotkeyKey.D] = 0x02, [HotkeyKey.F] = 0x03,
        [HotkeyKey.H] = 0x04, [HotkeyKey.G] = 0x05, [HotkeyKey.Z] = 0x06, [HotkeyKey.X] = 0x07,
        [HotkeyKey.C] = 0x08, [HotkeyKey.V] = 0x09, [HotkeyKey.B] = 0x0B, [HotkeyKey.Q] = 0x0C,
        [HotkeyKey.W] = 0x0D, [HotkeyKey.E] = 0x0E, [HotkeyKey.R] = 0x0F, [HotkeyKey.Y] = 0x10,
        [HotkeyKey.T] = 0x11, [HotkeyKey.D1] = 0x12, [HotkeyKey.D2] = 0x13, [HotkeyKey.D3] = 0x14,
        [HotkeyKey.D4] = 0x15, [HotkeyKey.D6] = 0x16, [HotkeyKey.D5] = 0x17, [HotkeyKey.D9] = 0x19,
        [HotkeyKey.D7] = 0x1A, [HotkeyKey.D8] = 0x1C, [HotkeyKey.D0] = 0x1D, [HotkeyKey.O] = 0x1F,
        [HotkeyKey.U] = 0x20, [HotkeyKey.I] = 0x22, [HotkeyKey.P] = 0x23, [HotkeyKey.L] = 0x25,
        [HotkeyKey.J] = 0x26, [HotkeyKey.K] = 0x28, [HotkeyKey.N] = 0x2D, [HotkeyKey.M] = 0x2E,
        [HotkeyKey.F1] = 0x7A, [HotkeyKey.F2] = 0x78, [HotkeyKey.F3] = 0x63, [HotkeyKey.F4] = 0x76,
        [HotkeyKey.F5] = 0x60, [HotkeyKey.F6] = 0x61, [HotkeyKey.F7] = 0x62, [HotkeyKey.F8] = 0x64,
        [HotkeyKey.F9] = 0x65, [HotkeyKey.F10] = 0x6D, [HotkeyKey.F11] = 0x67, [HotkeyKey.F12] = 0x6F,
    };

    /// <summary>Null for keys a Mac keyboard doesn't have (Print Screen).</summary>
    public static uint? ToKeyCode(HotkeyKey key) => Codes.TryGetValue(key, out var code) ? code : null;
}
