using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Screencap.App.Services;

internal static class Icons
{
    /// <summary>
    /// Looks up a glyph from Icons.axaml. Uses the application resources directly, because
    /// controls built in code are not yet in a window and can't resolve resources themselves.
    /// </summary>
    public static Geometry Get(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true && value is Geometry geometry
            ? geometry
            : throw new KeyNotFoundException($"Icon '{key}' not found in Icons.axaml");
}
