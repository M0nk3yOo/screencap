using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Screencap.Core.Updates;

/// <summary>Downloads a release package and swaps it in for the running app.</summary>
public sealed class UpdateInstaller
{
    private readonly HttpClient _http;

    public UpdateInstaller(HttpClient http) => _http = http;

    /// <summary>Downloads the asset into <paramref name="directory"/> and checks size and SHA-256.</summary>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string directory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!asset.Url.StartsWith(UpdateChecker.DownloadPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Der Download stammt nicht aus dem Screencap-Repository.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(asset.Name));

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.UserAgent.ParseAdd("Screencap");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        using var sha = SHA256.Create();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(path))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sha.TransformBlock(buffer, 0, read, null, 0);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
            if (asset.Size > 0 && done != asset.Size)
                throw new InvalidDataException("Der Download ist unvollständig.");
        }
        sha.TransformFinalBlock([], 0, 0);
        if (asset.Sha256 is { } expected && !Convert.ToHexString(sha.Hash!).Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException("Die Prüfsumme des Downloads stimmt nicht überein.");
        }
        return path;
    }

    // ---------------------------------------------------------------- Windows

    /// <summary>Unpacks Screencap.exe from the release ZIP into <paramref name="directory"/>.</summary>
    public static string ExtractWindowsExe(string zipPath, string directory)
    {
        Directory.CreateDirectory(directory);
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("Screencap.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Das Paket enthält keine Screencap.exe.");
        var path = Path.Combine(directory, "Screencap.exe");
        entry.ExtractToFile(path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Windows doesn't allow overwriting a running exe but does allow renaming it, so the old
    /// file is moved aside (and deleted on the next start) and the new one takes its place.
    /// </summary>
    public static void ReplaceExecutable(string currentExe, string newExe)
    {
        var backup = BackupPath(currentExe);
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(currentExe, backup);
        try
        {
            File.Move(newExe, currentExe);
        }
        catch
        {
            File.Move(backup, currentExe);
            throw;
        }
    }

    /// <summary>Removes the leftover of a previous update.</summary>
    public static void CleanUp(string currentExe)
    {
        try
        {
            if (File.Exists(BackupPath(currentExe))) File.Delete(BackupPath(currentExe));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use or read-only; the next start tries again.
        }
    }

    private static string BackupPath(string exe) => exe + ".old";

    /// <summary>Starts the new exe after the running one had time to quit (it still holds the hotkeys).</summary>
    public static void RelaunchWindows(string exe)
    {
        var quoted = exe.Replace("'", "''");
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add($"Start-Sleep -Seconds 2; Start-Process -FilePath '{quoted}'");
        Process.Start(info);
    }

    // ---------------------------------------------------------------- macOS

    /// <summary>The Screencap.app that contains the running binary, or null for a development build.</summary>
    public static string? FindBundle(string baseDirectory)
    {
        var dir = new DirectoryInfo(baseDirectory);
        while (dir is not null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Copies Screencap.app out of the DMG into a hidden folder next to the installed app (same
    /// volume, so the swap is a rename). Returns the path of the copy.
    /// </summary>
    public static async Task<string> StageMacAppAsync(string dmgPath, string bundlePath, string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var staged = Path.Combine(Path.GetDirectoryName(bundlePath)!, "." + Path.GetFileName(bundlePath) + ".update");
        var mount = Path.Combine(workDirectory, "mount");
        Directory.CreateDirectory(mount);
        await RunAsync("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount, dmgPath], cancellationToken);
        try
        {
            var source = Path.Combine(mount, Path.GetFileName(bundlePath));
            if (!Directory.Exists(source)) throw new InvalidDataException("Das Disk-Image enthält keine Screencap.app.");
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            await RunAsync("/usr/bin/ditto", [source, staged], cancellationToken);
        }
        finally
        {
            await RunAsync("/usr/bin/hdiutil", ["detach", mount, "-force"], CancellationToken.None, throwOnError: false);
        }
        try
        {
            await RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", staged], cancellationToken);
        }
        catch
        {
            Directory.Delete(staged, true);
            throw;
        }
        return staged;
    }

    /// <summary>
    /// Starts a detached script that waits for this process to quit, swaps the bundles and
    /// opens the new app. <paramref name="launcher"/> is /usr/bin/open except in tests.
    /// </summary>
    public static void StartMacSwap(int processId, string bundlePath, string stagedPath, string scriptDirectory,
        string launcher = "/usr/bin/open")
    {
        Directory.CreateDirectory(scriptDirectory);
        var script = Path.Combine(scriptDirectory, "swap.sh");
        File.WriteAllText(script, MacSwapScript);
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add("-c");
        // Backgrounded and detached, so it survives the quit of this process.
        info.ArgumentList.Add("nohup /bin/sh \"$0\" \"$@\" >/dev/null 2>&1 &");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add(processId.ToString());
        info.ArgumentList.Add(bundlePath);
        info.ArgumentList.Add(stagedPath);
        info.ArgumentList.Add(launcher);
        Process.Start(info)!.Dispose();
    }

    internal const string MacSwapScript = """
        #!/bin/sh
        # args: pid of the running app, installed bundle, new bundle, launcher
        PID="$1"; APP="$2"; NEW="$3"; LAUNCHER="$4"
        BACKUP="$(dirname "$APP")/.$(basename "$APP").old"
        i=0
        while kill -0 "$PID" 2>/dev/null && [ "$i" -lt 150 ]; do sleep 0.2; i=$((i+1)); done
        rm -rf "$BACKUP"
        if mv "$APP" "$BACKUP"; then
          if mv "$NEW" "$APP"; then
            rm -rf "$BACKUP"
          else
            mv "$BACKUP" "$APP"
          fi
        fi
        rm -rf "$NEW"
        "$LAUNCHER" "$APP"
        """;

    private static async Task RunAsync(string file, string[] args, CancellationToken cancellationToken, bool throwOnError = true)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new IOException($"{file} konnte nicht gestartet werden.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (throwOnError && process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(file)} schlug fehl: {(await error).Trim()}");
    }
}
