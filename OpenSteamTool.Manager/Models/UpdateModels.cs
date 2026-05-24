namespace OpenSteamTool.Manager.Models;

public sealed class UpdateCheckResult
{
    public Version CurrentVersion { get; init; } = new(0, 0, 0, 0);
    public Version? LatestVersion { get; init; }
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseTag { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public string ReleasePageUrl { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string AssetDownloadUrl { get; init; } = string.Empty;
    public bool HasRelease { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string? ErrorMessage { get; init; }

    public bool HasAsset => !string.IsNullOrWhiteSpace(AssetDownloadUrl);
}

public sealed class UpdateProgress
{
    public string Stage { get; init; } = string.Empty;
    public int Percent { get; init; }
    public string Message { get; init; } = string.Empty;
}
