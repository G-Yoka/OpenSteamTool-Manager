namespace OpenSteamTool.Manager.Models;

public sealed class ManagerSettings
{
    public bool GitHubDnsOptimizationEnabled { get; set; } = true;
    public List<GameResourceManifestSource> GameResourceManifestSources { get; set; } = new();
    public DateTimeOffset? LastSavedAtUtc { get; set; }
}

public sealed class GameResourceManifestSource
{
    public int Order { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
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
