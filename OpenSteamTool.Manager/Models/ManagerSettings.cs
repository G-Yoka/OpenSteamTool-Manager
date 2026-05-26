namespace OpenSteamTool.Manager.Models;

public enum NetworkSourcePriority
{
    CdnFirst = 0,
    GitHubFirst = 1
}

public sealed class ManagerSettings
{
    public NetworkSourcePriority NetworkSourcePriority { get; set; } = NetworkSourcePriority.CdnFirst;
    public DateTimeOffset? LastSavedAtUtc { get; set; }
}

public sealed class ConnectivityTestResult
{
    public string Source { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public string Detail { get; set; } = string.Empty;

    public string DurationText => $"{DurationMs} ms";
}
