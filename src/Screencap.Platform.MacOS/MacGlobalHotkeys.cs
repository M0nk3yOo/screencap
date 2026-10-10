using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Screencap.Core.Platform;
using Screencap.Core.Settings;

namespace Screencap.Platform.MacOS;

/// <summary>
/// Global hotkeys via Carbon's RegisterEventHotKey. Despite its age it is still what macOS
/// apps use for this: it needs no Accessibility permission, unlike event taps.
/// Callbacks arrive on the main thread.
/// </summary>
public sealed unsafe partial class MacGlobalHotkeys : IGlobalHotkeys
{
    private const string Lib = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Signature = 0x73636170; // 'scap'
    private const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint EventHotKeyPressed = 5;
    private const uint ParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'

    private static readonly Dictionary<uint, Action> Callbacks = [];
    private readonly Dictionary<uint, IntPtr> _registered = [];
    private IntPtr _handlerRef;
    private uint _nextId = 1;

    public bool TryRegister(Hotkey hotkey, Action onPressed)
    {
        if (MacKeyCodes.ToKeyCode(hotkey.Key) is not { } keyCode) return false;
        EnsureHandlerInstalled();

        var id = _nextId++;
        var status = RegisterEventHotKey(keyCode, ToCarbonModifiers(hotkey.Modifiers),
            new EventHotKeyId { Signature = Signature, Id = id }, GetApplicationEventTarget(), 0, out var hotKeyRef);
        if (status != 0) return false;

        _registered[id] = hotKeyRef;
        Callbacks[id] = onPressed;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var (id, hotKeyRef) in _registered)
        {
            UnregisterEventHotKey(hotKeyRef);
            Callbacks.Remove(id);
        }
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_handlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(_handlerRef);
            _handlerRef = IntPtr.Zero;
        }
    }

    private void EnsureHandlerInstalled()
    {
        if (_handlerRef != IntPtr.Zero) return;
        var spec = new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed };
        var status = InstallEventHandler(GetApplicationEventTarget(), &OnHotKey, 1, &spec, IntPtr.Zero, out _handlerRef);
        if (status != 0) throw new InvalidOperationException($"InstallEventHandler failed: {status}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        EventHotKeyId hotKeyId;
        var status = GetEventParameter(theEvent, ParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
            (nuint)sizeof(EventHotKeyId), IntPtr.Zero, &hotKeyId);
        if (status == 0 && hotKeyId.Signature == Signature && Callbacks.TryGetValue(hotKeyId.Id, out var callback))
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
        }
        return 0;
    }

    private static uint ToCarbonModifiers(HotkeyModifiers m)
    {
        uint result = 0;
        if (m.HasFlag(HotkeyModifiers.Meta)) result |= 0x0100; // cmdKey
        if (m.HasFlag(HotkeyModifiers.Shift)) result |= 0x0200; // shiftKey
        if (m.HasFlag(HotkeyModifiers.Alt)) result |= 0x0800; // optionKey
        if (m.HasFlag(HotkeyModifiers.Control)) result |= 0x1000; // controlKey
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [LibraryImport(Lib)]
    private static partial IntPtr GetApplicationEventTarget();

    [LibraryImport(Lib)]
    private static partial int InstallEventHandler(IntPtr target,
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int> handler,
        uint numTypes, EventTypeSpec* typeList, IntPtr userData, out IntPtr handlerRef);

    [LibraryImport(Lib)]
    private static partial int RemoveEventHandler(IntPtr handlerRef);

    [LibraryImport(Lib)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id,
        IntPtr target, uint options, out IntPtr hotKeyRef);

    [LibraryImport(Lib)]
    private static partial int UnregisterEventHotKey(IntPtr hotKeyRef);

    [LibraryImport(Lib)]
    private static partial int GetEventParameter(IntPtr theEvent, uint name, uint desiredType,
        IntPtr actualType, nuint bufferSize, IntPtr actualSize, EventHotKeyId* data);
}
