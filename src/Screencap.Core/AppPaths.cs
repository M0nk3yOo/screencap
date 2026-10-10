namespace Screencap.Core;

public static class AppPaths
{
    /// <summary>~/Library/Application Support/Screencap on macOS, %APPDATA%\Screencap on Windows.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Screencap");

    public const string SettingsFileName = "settings.json";

    public const string SessionDirectoryName = "session";

    public static string LogFile => Path.Combine(DataDirectory, "screencap.log");
}
