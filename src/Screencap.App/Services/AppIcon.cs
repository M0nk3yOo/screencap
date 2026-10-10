using Avalonia.Controls;
using Avalonia.Platform;

namespace Screencap.App.Services;

internal static class AppIcon
{
    private static WindowIcon? _icon;

    /// <summary>Window/taskbar icon (Windows). macOS takes the Dock icon from the app bundle.</summary>
    public static WindowIcon Window => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://Screencap/Assets/AppIcon.ico")));
}
