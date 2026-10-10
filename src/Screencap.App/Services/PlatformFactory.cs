using Screencap.Core.Platform;
using Screencap.Platform.MacOS;
#if WINDOWS
using Screencap.Platform.Windows;
#endif

namespace Screencap.App.Services;

internal static class PlatformFactory
{
    public static IPlatformServices Create()
    {
        if (OperatingSystem.IsMacOS()) return new MacPlatformServices();
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new WindowsPlatformServices();
#endif
        throw new PlatformNotSupportedException(OperatingSystem.IsWindows()
            ? "Unter Windows bitte die Windows-Variante starten (-f net10.0-windows10.0.19041.0)."
            : "Screencap läuft auf macOS und Windows.");
    }
}
