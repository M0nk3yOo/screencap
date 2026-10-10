using System.Text.Json;
using System.Text.Json.Serialization;

namespace Screencap.Core.Settings;

public sealed class SettingsStore(string path)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Missing or corrupt files fall back to defaults; settings must never block startup.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Settings could not be read, using defaults: {e.Message}");
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
