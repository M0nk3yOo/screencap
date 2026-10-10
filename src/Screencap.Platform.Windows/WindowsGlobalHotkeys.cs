using System.Runtime.InteropServices;
using Screencap.Core.Platform;
using Screencap.Core.Settings;

namespace Screencap.Platform.Windows;

/// <summary>
/// RegisterHotKey on a hidden message-only window. The window is created on the calling
/// (UI) thread, so Avalonia's message loop dispatches WM_HOTKEY to it.
/// </summary>
public sealed unsafe class WindowsGlobalHotkeys : IGlobalHotkeys
{
    private const string ClassName = "ScreencapHotkeyWindow";
    private static readonly Dictionary<int, Action> Callbacks = [];
    private static bool _classRegistered;
    private IntPtr _hwnd;
    private int _nextId = 1;

    public bool TryRegister(Hotkey hotkey, Action onPressed)
    {
        EnsureWindow();
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_hwnd, id, ToModifiers(hotkey.Modifiers) | Win32.MOD_NOREPEAT, ToVirtualKey(hotkey.Key)))
            return false;
        Callbacks[id] = onPressed;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in Callbacks.Keys) Win32.UnregisterHotKey(_hwnd, id);
        Callbacks.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_hwnd != IntPtr.Zero) Win32.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;
        var instance = Win32.GetModuleHandleW(null);
        if (!_classRegistered)
        {
            fixed (char* name = ClassName)
            {
                var wc = new Win32.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = instance,
                    lpszClassName = name,
                };
                if (Win32.RegisterClassExW(&wc) == 0)
                    throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
            }
            _classRegistered = true;
        }
        _hwnd = Win32.CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Hotkey window could not be created");
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_HOTKEY && Callbacks.TryGetValue((int)wParam, out var callback))
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                // Exceptions must not cross back into native code.
                Console.Error.WriteLine($"Hotkey handler failed: {e}");
            }
            return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static uint ToModifiers(HotkeyModifiers m)
    {
        uint result = 0;
        if (m.HasFlag(HotkeyModifiers.Alt)) result |= 0x1;
        if (m.HasFlag(HotkeyModifiers.Control)) result |= 0x2;
        if (m.HasFlag(HotkeyModifiers.Shift)) result |= 0x4;
        if (m.HasFlag(HotkeyModifiers.Meta)) result |= 0x8;
        return result;
    }

    private static uint ToVirtualKey(HotkeyKey key) => key switch
    {
        >= HotkeyKey.D0 and <= HotkeyKey.D9 => 0x30 + (uint)(key - HotkeyKey.D0),
        >= HotkeyKey.A and <= HotkeyKey.Z => 0x41 + (uint)(key - HotkeyKey.A),
        >= HotkeyKey.F1 and <= HotkeyKey.F12 => 0x70 + (uint)(key - HotkeyKey.F1),
        HotkeyKey.PrintScreen => 0x2C,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}
