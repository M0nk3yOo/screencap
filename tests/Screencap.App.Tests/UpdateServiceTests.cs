using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Screencap.App.Services;
using Screencap.Core;
using Screencap.Core.Updates;

namespace Screencap.App.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"screencap-apptest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, true);
    }

    private static string Json(string tag) => $$"""
        {"tag_name": "{{tag}}", "draft": false, "prerelease": false, "html_url": "https://github.com/M0nk3yOo/screencap/releases/tag/{{tag}}",
         "body": "Neu", "assets": [{"name": "Screencap-{{tag.TrimStart('v')}}-windows-x64.zip", "size": 1,
         "browser_download_url": "https://github.com/M0nk3yOo/screencap/releases/download/{{tag}}/Screencap-{{tag.TrimStart('v')}}-windows-x64.zip"}]}
        """;

    private static UpdateService Service(Func<HttpResponseMessage> answer, string? processPath = null)
    {
        var http = new HttpClient(new Stub(answer));
        return new UpdateService(new UpdateChecker(http), new UpdateInstaller(http), UpdateTarget.WindowsX64,
            processPath ?? Path.Combine(Path.GetTempPath(), "Screencap.exe"), AppContext.BaseDirectory);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [AvaloniaFact]
    public async Task Check_FindsNewerReleaseWithPackage()
    {
        var service = Service(() => Ok(Json("v99.0.0")));
        var raised = 0;
        service.AvailableChanged += () => raised++;

        Assert.Equal(UpdateCheckResult.Available, await service.CheckAsync());

        Assert.Equal(new Version(99, 0, 0), service.Available!.Version);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public async Task Check_OlderOrSameVersionIsUpToDate()
    {
        Assert.Equal(UpdateCheckResult.UpToDate, await Service(() => Ok(Json("v0.0.1"))).CheckAsync());
        Assert.Equal(UpdateCheckResult.UpToDate, await Service(() => Ok(Json("v" + UpdateChecker.CurrentVersion.ToString(3)))).CheckAsync());
    }

    [AvaloniaFact]
    public async Task Check_WaitsUntilThePackageForThisSystemIsUploaded()
    {
        // The Windows packages are attached a few minutes after the release is published.
        var json = Json("v99.0.0").Replace("windows-x64", "windows-other");
        var service = Service(() => Ok(json));

        Assert.Equal(UpdateCheckResult.UpToDate, await service.CheckAsync());
        Assert.Null(service.Available);
    }

    [AvaloniaFact]
    public async Task Check_ReportsNetworkErrors()
    {
        var service = Service(() => throw new HttpRequestException("offline"));

        Assert.Equal(UpdateCheckResult.Failed, await service.CheckAsync());
        Assert.Equal("offline", service.LastError);
    }

    [AvaloniaFact]
    public void CanInstall_OnlyForTheInstalledApp()
    {
        Assert.True(Service(() => Ok("{}"), Path.Combine(_dataDirectory, "Screencap.exe")).CanInstall);
        // Running from `dotnet run`: no update, nothing to replace.
        Assert.False(Service(() => Ok("{}"), Path.Combine(_dataDirectory, "dotnet.exe")).CanInstall);
    }

    [AvaloniaFact]
    public async Task AutomaticCheck_StaysQuietForSkippedVersion_ManualCheckAlwaysShows()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.Updates = Service(() => Ok(Json("v99.0.0")));
        app.Settings.SkippedUpdateVersion = "99.0.0";

        await app.CheckForUpdatesAsync(userInitiated: false);
        Assert.False(app.IsUpdateWindowOpen);

        await app.CheckForUpdatesAsync(userInitiated: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.IsUpdateWindowOpen);
    }

    [AvaloniaFact]
    public async Task AutomaticCheck_AnnouncesNewVersion()
    {
        var app = new AppController(null!, new FakePlatform(), _dataDirectory);
        app.Updates = Service(() => Ok(Json("v99.0.0")));

        await app.CheckForUpdatesAsync(userInitiated: false);

        Assert.True(app.IsUpdateWindowOpen);
        foreach (var w in ((Avalonia.Application.Current!.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows ?? []).ToList())
            w.Close();
    }

    [AvaloniaFact]
    public void UpdateRestart_KeepsOpenScreenshotsOnce()
    {
        var first = new AppController(null!, new FakePlatform(), _dataDirectory);
        first.OpenInEditor(new Capture(TestImages.Solid(40, 30), 1));
        first.Settings.RestoreTabsOnce = true;
        first.SaveSettings();
        first.Shutdown();

        var second = new AppController(null!, new FakePlatform(), _dataDirectory);
        second.Start();

        Assert.Single(second.Tabs);
        Assert.False(second.Settings.RestoreTabsOnce);
        second.Shutdown();

        // The next start without the setting begins empty again.
        var third = new AppController(null!, new FakePlatform(), _dataDirectory);
        third.Start();
        Assert.Empty(third.Tabs);
        third.Shutdown();
    }

    private sealed class Stub(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer());
    }
}
