using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Screencap.Core.Updates;

namespace Screencap.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"screencap-update-{Guid.NewGuid():N}");

    public UpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private const string Prefix = "https://github.com/M0nk3yOo/screencap/releases/download/v1.0.5/";

    private static string ReleaseJson(string tag = "v1.0.5", bool prerelease = false, string evilUrl = "") => $$"""
        {
          "tag_name": "{{tag}}", "draft": false, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/M0nk3yOo/screencap/releases/tag/{{tag}}",
          "body": "Neu: Esc.\n\nDetails: [Changelog](CHANGELOG.md)",
          "assets": [
            {"name": "Screencap-1.0.5-macos-apple-silicon.dmg", "size": 10, "digest": "sha256:ABCDEF", "browser_download_url": "{{Prefix}}Screencap-1.0.5-macos-apple-silicon.dmg"},
            {"name": "Screencap-1.0.5-windows-x64.zip", "size": 20, "browser_download_url": "{{Prefix}}Screencap-1.0.5-windows-x64.zip"},
            {"name": "Screencap-1.0.5-windows-arm64.zip", "size": 30, "browser_download_url": "{{evilUrl}}"}
          ]
        }
        """;

    [Theory]
    [InlineData("v1.0.5", "1.0.5")]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData("2.0.0-beta.1", "2.0.0")]
    public void ParseVersion_StripsPrefixAndSuffix(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(text));

    [Fact]
    public void ParseVersion_RejectsGarbage()
    {
        Assert.Null(UpdateChecker.ParseVersion("nightly"));
        Assert.Null(UpdateChecker.ParseVersion(""));
        Assert.Null(UpdateChecker.ParseVersion(null));
    }

    [Fact]
    public void Parse_ReadsReleaseAndDigest()
    {
        var release = UpdateChecker.Parse(ReleaseJson())!;
        Assert.Equal(new Version(1, 0, 5), release.Version);
        Assert.StartsWith("Neu: Esc.", release.Notes);
        var mac = UpdateChecker.SelectAsset(release, UpdateTarget.MacAppleSilicon)!;
        Assert.Equal("abcdef", mac.Sha256);
        Assert.Equal(10, mac.Size);
        Assert.Null(UpdateChecker.SelectAsset(release, UpdateTarget.WindowsX64)!.Sha256);
    }

    [Fact]
    public void Parse_DropsAssetsFromOtherLocations()
    {
        var release = UpdateChecker.Parse(ReleaseJson(evilUrl: "https://evil.example/Screencap-1.0.5-windows-arm64.zip"))!;
        Assert.Null(UpdateChecker.SelectAsset(release, UpdateTarget.WindowsArm64));
        Assert.NotNull(UpdateChecker.SelectAsset(release, UpdateTarget.WindowsX64));
    }

    [Fact]
    public void Parse_IgnoresPrereleases() => Assert.Null(UpdateChecker.Parse(ReleaseJson(prerelease: true)));

    [Fact]
    public async Task GetLatest_Reads404AsNoRelease()
    {
        var checker = new UpdateChecker(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await checker.GetLatestAsync());
    }

    [Fact]
    public async Task GetLatest_SendsUserAgentAndParses()
    {
        HttpRequestMessage? seen = null;
        var checker = new UpdateChecker(new HttpClient(new StubHandler(r =>
        {
            seen = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson()) };
        })));
        var release = await checker.GetLatestAsync();
        Assert.Equal(new Version(1, 0, 5), release!.Version);
        Assert.Contains("Screencap", seen!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Download_VerifiesChecksum()
    {
        var data = new byte[50_000];
        new Random(1).NextBytes(data);
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var installer = new UpdateInstaller(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) })));
        var progress = new List<double>();

        var good = new ReleaseAsset("Screencap-1.0.5-windows-x64.zip", Prefix + "Screencap-1.0.5-windows-x64.zip", data.Length, sha);
        var path = await installer.DownloadAsync(good, _dir, new SyncProgress(progress.Add));
        Assert.Equal(data, File.ReadAllBytes(path));
        Assert.Equal(1.0, progress[^1]);

        var bad = good with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.DownloadAsync(bad, _dir));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Download_RejectsForeignUrlsAndTruncatedFiles()
    {
        var installer = new UpdateInstaller(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) })));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.DownloadAsync(new ReleaseAsset("x.zip", "https://evil.example/x.zip", 10, null), _dir));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.DownloadAsync(new ReleaseAsset("x.zip", Prefix + "x.zip", 999, null), _dir));
    }

    [Fact]
    public void ReplaceExecutable_SwapsAndKeepsBackupUntilCleanUp()
    {
        var exe = Path.Combine(_dir, "Screencap.exe");
        var next = Path.Combine(_dir, "new.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(next, "new");

        UpdateInstaller.ReplaceExecutable(exe, next);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(exe + ".old"));
        UpdateInstaller.CleanUp(exe);
        Assert.False(File.Exists(exe + ".old"));
    }

    [Fact]
    public void ReplaceExecutable_RestoresOldFileWhenNewOneIsMissing()
    {
        var exe = Path.Combine(_dir, "Screencap.exe");
        File.WriteAllText(exe, "old");

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.ReplaceExecutable(exe, Path.Combine(_dir, "missing.exe")));

        Assert.Equal("old", File.ReadAllText(exe));
    }

    [Fact]
    public void ExtractWindowsExe_TakesOnlyTheExe()
    {
        var zip = Path.Combine(_dir, "pkg.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(archive.CreateEntry("Screencap.exe").Open());
            w.Write("binary");
        }
        var path = UpdateInstaller.ExtractWindowsExe(zip, Path.Combine(_dir, "out"));
        Assert.Equal("binary", File.ReadAllText(path));

        var empty = Path.Combine(_dir, "empty.zip");
        ZipFile.Open(empty, ZipArchiveMode.Create).Dispose();
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.ExtractWindowsExe(empty, Path.Combine(_dir, "out2")));
    }

    [Fact]
    public void FindBundle_WalksUpToTheAppFolder()
    {
        var inside = Path.Combine(_dir, "Screencap.app", "Contents", "MacOS");
        Assert.Equal(Path.Combine(_dir, "Screencap.app"), UpdateInstaller.FindBundle(inside + Path.DirectorySeparatorChar));
        Assert.Null(UpdateInstaller.FindBundle(Path.Combine(_dir, "bin", "Debug")));
    }

    [Fact]
    public async Task MacSwap_ReplacesBundleAfterProcessExit_AndKeepsBackupOnFailure()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var app = Path.Combine(_dir, "Screencap.app");
        var staged = Path.Combine(_dir, ".Screencap.app.update");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "version"), "old");
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "version"), "new");
        var marker = Path.Combine(_dir, "launched");
        var launcher = Path.Combine(_dir, "launcher.sh");
        File.WriteAllText(launcher, $"#!/bin/sh\necho \"$1\" > \"{marker}\"\n");
        File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        // A process that is already gone: the script must not wait for it.
        var gone = Process.Start("/usr/bin/true")!;
        await gone.WaitForExitAsync();
        UpdateInstaller.StartMacSwap(gone.Id, app, staged, Path.Combine(_dir, "scripts"), launcher);

        for (var i = 0; i < 100 && !File.Exists(marker); i++) await Task.Delay(50);
        Assert.True(File.Exists(marker), "launcher was not run");
        Assert.Equal(app, File.ReadAllText(marker).Trim());
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "version")));
        Assert.False(Directory.Exists(staged));
        Assert.False(Directory.Exists(Path.Combine(_dir, ".Screencap.app.old")));
    }

    [Fact]
    public async Task MacSwap_WaitsForTheRunningAppToQuit()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var app = Path.Combine(_dir, "Screencap.app");
        var staged = Path.Combine(_dir, ".Screencap.app.update");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "version"), "old");
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "version"), "new");

        var running = Process.Start("/bin/sleep", "2")!;
        UpdateInstaller.StartMacSwap(running.Id, app, staged, Path.Combine(_dir, "scripts"), "/usr/bin/touch");
        await Task.Delay(700);
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "version")));

        await running.WaitForExitAsync();
        for (var i = 0; i < 100 && Directory.Exists(staged); i++) await Task.Delay(50);
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "version")));
    }

    [Fact]
    public async Task StageMacApp_CopiesSignedAppOutOfTheDiskImage()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var src = Path.Combine(_dir, "dmgsrc");
        var app = Path.Combine(src, "Screencap.app");
        Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"),
            "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleExecutable</key><string>Screencap</string>" +
            "<key>CFBundleIdentifier</key><string>app.screencap.test</string></dict></plist>");
        var exe = Path.Combine(app, "Contents", "MacOS", "Screencap");
        File.WriteAllText(exe, "#!/bin/sh\n");
        File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Run("/usr/bin/codesign", "--force", "--sign", "-", app);
        var dmg = Path.Combine(_dir, "test.dmg");
        Run("/usr/bin/hdiutil", "create", "-volname", "Test", "-srcfolder", src, "-format", "UDZO", "-ov", dmg);

        var installed = Path.Combine(_dir, "Applications", "Screencap.app");
        Directory.CreateDirectory(installed);
        var staged = await UpdateInstaller.StageMacAppAsync(dmg, installed, Path.Combine(_dir, "work"));

        Assert.Equal(Path.Combine(_dir, "Applications", ".Screencap.app.update"), staged);
        Assert.True(File.Exists(Path.Combine(staged, "Contents", "MacOS", "Screencap")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "work", "mount", "Screencap.app")), "image was not detached");
    }

    private static void Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
