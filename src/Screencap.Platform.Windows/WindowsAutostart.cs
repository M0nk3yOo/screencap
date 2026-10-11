using Microsoft.Win32;
using Screencap.Core.Platform;

namespace Screencap.Platform.Windows;

/// <summary>Start at login via the per-user Run key (also visible in Task Manager → Startup apps).</summary>
public sealed class WindowsAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Screencap";

    /// <summary>Only the published Screencap.exe can be registered, not `dotnet run`.</summary>
    private static string? Executable =>
        Environment.ProcessPath is { } path && Path.GetFileName(path).Equals("Screencap.exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;

    public bool IsSupported => Executable is not null;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        else if (Executable is { } executable)
            key.SetValue(ValueName, $"\"{executable}\"");
    }
}
