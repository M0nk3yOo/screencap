namespace Screencap.Core.Updates;

/// <summary>A downloadable file of a release. <see cref="Sha256"/> is null when GitHub gave no digest.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>The package of a release that fits the running system.</summary>
public enum UpdateTarget
{
    MacAppleSilicon,
    WindowsX64,
    WindowsArm64,
}
