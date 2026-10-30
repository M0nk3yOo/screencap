using System.Net.Http;
using System.Text.Json;
using Screencap.Core;
using Screencap.Core.Updates;

namespace Screencap.App.Services;

internal enum UpdateCheckResult
{
    UpToDate,
    Available,
    Failed,
}

/// <summary>Looks for newer releases and installs them over the running app.</summary>
internal sealed class UpdateService
{
    private readonly UpdateChecker _checker;
    private readonly UpdateInstaller _installer;
    private readonly UpdateTarget? _target;
    private readonly string? _exePath;
    private readonly string? _bundlePath;

    public UpdateService(UpdateChecker checker, UpdateInstaller installer, UpdateTarget? target,
        string? processPath, string baseDirectory)
    {
        _checker = checker;
        _installer = installer;
        _target = target;
        _exePath = target is UpdateTarget.WindowsX64 or UpdateTarget.WindowsArm64
            && string.Equals(Path.GetFileName(processPath), "Screencap.exe", StringComparison.OrdinalIgnoreCase)
            ? processPath : null;
        _bundlePath = target == UpdateTarget.MacAppleSilicon ? UpdateInstaller.FindBundle(baseDirectory) : null;
    }

    public static UpdateService CreateDefault() => new(
        new UpdateChecker(Http), new UpdateInstaller(Http), UpdateChecker.CurrentTarget(),
        Environment.ProcessPath, AppContext.BaseDirectory);

    // Downloads are large, so no overall timeout; every call has its own cancellation.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>False for development builds and systems without a package; they never check or install.</summary>
    public bool CanInstall => _exePath is not null || _bundlePath is not null;

    /// <summary>A newer release with a package for this system, once found.</summary>
    public ReleaseInfo? Available { get; private set; }

    public string? LastError { get; private set; }

    public event Action? AvailableChanged;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var latest = await _checker.GetLatestAsync(timeout.Token);
            LastError = null;
            var newer = latest is not null && latest.Version > UpdateChecker.CurrentVersion
                && _target is { } target && UpdateChecker.SelectAsset(latest, target) is not null;
            Available = newer ? latest : null;
            AvailableChanged?.Invoke();
            return newer ? UpdateCheckResult.Available : UpdateCheckResult.UpToDate;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Being offline is normal; the message is shown for manual checks only, not logged.
            LastError = e is TaskCanceledException ? "Zeitüberschreitung bei der Verbindung zu GitHub." : e.Message;
            return UpdateCheckResult.Failed;
        }
    }

    /// <summary>
    /// Downloads the available release and puts it in place. Returns the action that starts the
    /// new version; the caller then quits the app.
    /// </summary>
    public async Task<Action> PrepareAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var release = Available ?? throw new InvalidOperationException("Es ist kein Update verfügbar.");
        var asset = UpdateChecker.SelectAsset(release, _target ?? throw new InvalidOperationException("Kein Paket für dieses System."))
            ?? throw new InvalidOperationException("Für dieses System gibt es kein Paket.");
        var work = Path.Combine(Path.GetTempPath(), "screencap-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = await _installer.DownloadAsync(asset, work, progress, cancellationToken);
            if (_exePath is { } exe)
            {
                var newExe = UpdateInstaller.ExtractWindowsExe(file, work);
                UpdateInstaller.ReplaceExecutable(exe, newExe);
                return () => UpdateInstaller.RelaunchWindows(exe);
            }
            var bundle = _bundlePath ?? throw new InvalidOperationException("Dieses Update kann nur in der installierten App erfolgen.");
            var staged = await UpdateInstaller.StageMacAppAsync(file, bundle, work, cancellationToken);
            return () => UpdateInstaller.StartMacSwap(Environment.ProcessId, bundle, staged, Path.Combine(AppPaths.DataDirectory, "update"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Temp files; the OS cleans them up eventually.
            }
        }
    }

    /// <summary>Removes the leftover of the previous update (Windows renames the old exe).</summary>
    public void CleanUpAfterUpdate()
    {
        if (_exePath is { } exe) UpdateInstaller.CleanUp(exe);
    }
}
