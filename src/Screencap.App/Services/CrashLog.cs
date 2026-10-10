using Avalonia.Threading;
using Screencap.Core;

namespace Screencap.App.Services;

/// <summary>
/// Writes unhandled exceptions to a log file. Exceptions thrown inside native UI callbacks
/// otherwise abort the process without any message.
/// </summary>
internal static class CrashLog
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Unobserved task", e.Exception);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            // Keep the app alive; a failed click should not kill the menu bar app.
            e.Handled = true;
        };
    }

    public static void Write(string context, Exception? exception)
    {
        var entry = $"[{DateTimeOffset.Now:O}] {context}: {exception}{Environment.NewLine}";
        Console.Error.Write(entry);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.LogFile, entry);
        }
        catch (IOException)
        {
            // Logging must never throw.
        }
    }
}
