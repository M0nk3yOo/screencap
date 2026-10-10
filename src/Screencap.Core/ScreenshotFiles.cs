using System.Globalization;

namespace Screencap.Core;

/// <summary>Naming and housekeeping of saved screenshot files.</summary>
public static class ScreenshotFiles
{
    public const string Prefix = "Screencap ";
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];
    private const string TimestampFormat = "yyyy-MM-dd HH-mm-ss";

    /// <summary>"Screencap 2026-10-10 14-33-05.png" — sortable, with seconds.</summary>
    public static string FileName(DateTimeOffset timestamp, string extension = ".png") =>
        Prefix + timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture) + extension;

    /// <summary>Appends " (2)", " (3)" … if the name is taken, e.g. two captures in the same second.</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Deletes screenshots older than <paramref name="maxAge"/>. Only touches files that follow
    /// Screencap's own naming scheme, so other files in the folder are never deleted.
    /// </summary>
    /// <returns>Number of deleted files.</returns>
    public static int DeleteOlderThan(string folder, TimeSpan maxAge, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, Prefix + "*"))
        {
            if (!IsScreencapFile(Path.GetFileName(file))) continue;
            try
            {
                if (nowUtc - File.GetLastWriteTimeUtc(file) <= maxAge) continue;
                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Locked or protected files are skipped and retried next time.
            }
        }
        return deleted;
    }

    /// <summary>Matches "Screencap yyyy-MM-dd HH-mm-ss.png" (or .jpg), optionally with " (n)".</summary>
    internal static bool IsScreencapFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
        var rest = fileName[Prefix.Length..^extension.Length];
        if (rest.Length < TimestampFormat.Length) return false;
        var suffix = rest[TimestampFormat.Length..];
        return DateTime.TryParseExact(rest[..TimestampFormat.Length], TimestampFormat, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out _)
               && (suffix.Length == 0 || (suffix.StartsWith(" (", StringComparison.Ordinal) && suffix.EndsWith(')')));
    }
}
