using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Screencap.Core.Updates;

/// <summary>Finds out whether a newer release exists on GitHub (public API, no login needed).</summary>
public sealed class UpdateChecker
{
    public const string Repository = "M0nk3yOo/screencap";

    /// <summary>Downloads are only accepted from this prefix, whatever the API answers.</summary>
    public static readonly string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";

    private readonly HttpClient _http;
    private readonly Uri _latestUrl;

    public UpdateChecker(HttpClient http, string? latestUrl = null)
    {
        _http = http;
        _latestUrl = new Uri(latestUrl ?? $"https://api.github.com/repos/{Repository}/releases/latest");
    }

    /// <summary>Version of the running app (from the build, without the commit suffix).</summary>
    public static Version CurrentVersion { get; } = ParseVersion(
        typeof(UpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new Version(0, 0);

    /// <summary>The UpdateTarget of this machine, or null where no package exists (e.g. Linux, Intel Mac).</summary>
    public static UpdateTarget? CurrentTarget()
    {
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? UpdateTarget.MacAppleSilicon : null;
        if (OperatingSystem.IsWindows())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? UpdateTarget.WindowsArm64 : UpdateTarget.WindowsX64;
        return null;
    }

    /// <summary>The newest published release, or null if there is none.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Screencap", CurrentVersion.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = Text(root, "tag_name");
        if (ParseVersion(tag) is not { } version) return null;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var url = Text(a, "browser_download_url");
                if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                var digest = Text(a, "digest");
                var sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                assets.Add(new ReleaseAsset(Text(a, "name"), url, size, sha));
            }
        }
        return new ReleaseInfo(version, tag, Text(root, "body"), Text(root, "html_url"), assets);
    }

    /// <summary>"v1.0.5", "1.0.5+abc123" → 1.0.5; null if it isn't a version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['+', '-', ' ']);
        if (end >= 0) text = text[..end];
        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>The package for <paramref name="target"/>, e.g. Screencap-1.0.5-windows-x64.zip.</summary>
    public static ReleaseAsset? SelectAsset(ReleaseInfo release, UpdateTarget target)
    {
        var suffix = target switch
        {
            UpdateTarget.MacAppleSilicon => "-macos-apple-silicon.dmg",
            UpdateTarget.WindowsX64 => "-windows-x64.zip",
            _ => "-windows-arm64.zip",
        };
        return release.Assets.FirstOrDefault(a =>
            a.Name.StartsWith("Screencap-", StringComparison.Ordinal) && a.Name.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
