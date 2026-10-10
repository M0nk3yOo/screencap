namespace Screencap.Core.Tests;

public sealed class ScreenshotFilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("screencap-files-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void FileName_HasSecondsAndSortsChronologically()
    {
        var name = ScreenshotFiles.FileName(new DateTimeOffset(2026, 10, 10, 14, 33, 5, TimeSpan.Zero));
        Assert.Equal("Screencap 2026-10-10 14-33-05.png", name);
    }

    [Fact]
    public void UniquePath_AddsCounterOnCollision()
    {
        var path = Path.Combine(_folder, "Screencap 2026-10-10 14-33-05.png");
        File.WriteAllText(path, "");
        Assert.Equal(Path.Combine(_folder, "Screencap 2026-10-10 14-33-05 (2).png"), ScreenshotFiles.UniquePath(path));
    }

    [Fact]
    public void DeleteOlderThan_OnlyDeletesOldScreencapFiles()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        string Create(string name, int ageDays)
        {
            var path = Path.Combine(_folder, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, now.AddDays(-ageDays));
            return path;
        }

        var oldShot = Create("Screencap 2026-01-01 10-00-00.png", 200);
        var oldShotDuplicate = Create("Screencap 2026-01-01 10-00-00 (2).png", 200);
        var recentShot = Create("Screencap 2026-09-01 10-00-00.png", 30);
        var foreignOld = Create("Urlaub.png", 400);
        var lookalike = Create("Screencap notes.png", 400);

        var deleted = ScreenshotFiles.DeleteOlderThan(_folder, TimeSpan.FromDays(180), now);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(oldShot));
        Assert.False(File.Exists(oldShotDuplicate));
        Assert.True(File.Exists(recentShot));
        Assert.True(File.Exists(foreignOld));
        Assert.True(File.Exists(lookalike));
    }

    [Fact]
    public void DeleteOlderThan_IgnoresMissingFolder() =>
        Assert.Equal(0, ScreenshotFiles.DeleteOlderThan(Path.Combine(_folder, "missing"), TimeSpan.FromDays(1), DateTime.UtcNow));
}
