using System.Diagnostics;
using System.Runtime.InteropServices;
using Screencap.Core.Geometry;
using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

/// <summary>Visible top-level windows in z-order (EnumWindows enumerates front to back).</summary>
public sealed unsafe partial class WindowsWindowInspector : IWindowInspector
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;

    [ThreadStatic] private static List<IntPtr>? _collected;

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        _collected = [];
        EnumWindows(&Collect, IntPtr.Zero);
        var handles = _collected;
        _collected = null;

        var names = new Dictionary<uint, string>();
        var result = new List<WindowInfo>();
        foreach (var hwnd in handles)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;
            if ((GetWindowLongPtrW(hwnd, GwlExStyle) & WsExToolWindow) != 0) continue;
            int cloaked;
            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, &cloaked, sizeof(int)) == 0 && cloaked != 0) continue; // other virtual desktop, UWP placeholder

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) continue;

            Rect r;
            // Extended frame bounds exclude the invisible resize border around Windows 10/11 windows.
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, &r, sizeof(Rect)) != 0 && !GetWindowRect(hwnd, out r)) continue;

            if (!names.TryGetValue(pid, out var app)) names[pid] = app = AppName(pid);
            result.Add(new WindowInfo(app, Title(hwnd), new IntRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
        }
        return result;
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr hwnd, IntPtr lParam)
    {
        _collected?.Add(hwnd);
        return 1;
    }

    private static string Title(IntPtr hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        fixed (char* p = buffer)
            return new string(p, 0, GetWindowTextW(hwnd, p, buffer.Length));
    }

    /// <summary>Friendly app name ("Microsoft Edge") from the exe's version info, else the process name.</summary>
    private static string AppName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            try
            {
                if (process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description) return description;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Elevated or protected processes don't expose their modules.
            }
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(IntPtr hwnd, char* text, int maxCount);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, void* value, int size);
}
