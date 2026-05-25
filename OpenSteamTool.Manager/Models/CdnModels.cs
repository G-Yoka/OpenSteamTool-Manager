namespace OpenSteamTool.Manager.Models;

public sealed class CdnUpdateMetadata
{
    public int Version { get; set; } = 1;
    public string VersionTag { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseTag { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public string ReleasePageUrl { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
}
