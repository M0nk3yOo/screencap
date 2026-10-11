using System.Text.Json;
using Screencap.Core.Annotations;
using Screencap.Core.Geometry;
using Screencap.Core.Imaging;
using Screencap.Core.Settings;

namespace Screencap.Core.Session;

/// <summary>
/// Persists open editor tabs: one PNG per screenshot plus a JSON manifest with the annotations.
/// Images never change after capture, so they are written only once.
/// </summary>
public sealed class SessionStore(string directory)
{
    private const string ManifestName = "session.json";

    private sealed record Entry(Guid Id, DateTimeOffset CreatedAt, double Scaling, List<Annotation> Annotations, IntRect? Crop = null,
        string? SourceApp = null, string? SourceTitle = null);

    public void Save(IReadOnlyList<Capture> captures)
    {
        Directory.CreateDirectory(directory);

        foreach (var capture in captures)
        {
            var imagePath = ImagePath(capture.Id);
            if (!File.Exists(imagePath))
                File.WriteAllBytes(imagePath, capture.Image.EncodePng());
        }

        var entries = captures
            .Select(c => new Entry(c.Id, c.CreatedAt, c.Scaling, c.Annotations.Items.ToList(), c.Annotations.Crop, c.SourceApp, c.SourceTitle))
            .ToList();
        AtomicFile.WriteAllText(Path.Combine(directory, ManifestName),
            JsonSerializer.Serialize(entries, SettingsStore.JsonOptions));

        // Drop images of tabs that were closed.
        var keep = captures.Select(c => ImagePath(c.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
            if (!keep.Contains(file)) File.Delete(file);
    }

    public List<Capture> Load()
    {
        var manifest = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifest)) return [];

        List<Entry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(manifest), SettingsStore.JsonOptions);
        }
        catch (JsonException e)
        {
            Console.Error.WriteLine($"Session could not be read: {e.Message}");
            return [];
        }

        var result = new List<Capture>();
        foreach (var entry in entries ?? [])
        {
            var imagePath = ImagePath(entry.Id);
            if (!File.Exists(imagePath)) continue;
            result.Add(new Capture(PixelBuffer.Decode(File.ReadAllBytes(imagePath)), entry.Scaling)
            {
                Id = entry.Id,
                CreatedAt = entry.CreatedAt,
                Annotations = new AnnotationDocument(entry.Annotations, entry.Crop),
                SourceApp = entry.SourceApp,
                SourceTitle = entry.SourceTitle,
            });
        }
        return result;
    }

    public void Clear()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private string ImagePath(Guid id) => Path.Combine(directory, $"{id:N}.png");
}
