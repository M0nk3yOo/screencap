using System.Security;
using Screencap.Core.Platform;

namespace Screencap.Platform.MacOS;

/// <summary>
/// Start at login via a per-user LaunchAgent that runs the app's executable. Works for any
/// install location; macOS lists it under Login Items as "Screencap".
/// </summary>
public sealed class MacAutostart : IAutostart
{
    private const string Label = "app.screencap.Screencap";

    private static string AgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    /// <summary>The running executable, if it lives inside an app bundle (not `dotnet run`).</summary>
    private static string? Executable =>
        Environment.ProcessPath is { } path && path.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) ? path : null;

    public bool IsSupported => Executable is not null;

    public bool IsEnabled => File.Exists(AgentPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(AgentPath);
            return;
        }
        if (Executable is not { } executable) return;
        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        File.WriteAllText(AgentPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key><array><string>{SecurityElement.Escape(executable)}</string></array>
              <key>RunAtLoad</key><true/>
              <key>ProcessType</key><string>Interactive</string>
            </dict>
            </plist>
            """);
    }
}
