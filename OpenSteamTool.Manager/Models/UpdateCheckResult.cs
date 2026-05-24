namespace OpenSteamTool.Manager.Models;

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public bool HasRelease { get; set; } = true;
    public string? ErrorMessage { get; set; }
}