using System.IO;
using System.Text.Json;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class ManagerSettingsService
{
    private const string DefaultManifestSourceUrl = "https://raw.githubusercontent.com/G-Yoka/GameResources/main/manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSteamTool.Manager");
    private readonly string _settingsFile;
    private ManagerSettings _current = new();

    public ManagerSettingsService()
    {
        _settingsFile = Path.Combine(_settingsDirectory, "settings.json");
        Reload();
    }

    public string SettingsPath => _settingsFile;

    public ManagerSettings Current
    {
        get
        {
            lock (_sync)
            {
                return new ManagerSettings
                {
                    GitHubDnsOptimizationEnabled = _current.GitHubDnsOptimizationEnabled,
                    GameResourceManifestSources = _current.GameResourceManifestSources.Select(CloneSource).ToList(),
                    LastSavedAtUtc = _current.LastSavedAtUtc
                };
            }
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var settings = JsonSerializer.Deserialize<ManagerSettings>(json, JsonOptions);
                    _current = settings is null ? new ManagerSettings() : Normalize(settings);
                    return;
                }
            }
            catch
            {
                // Fall back to defaults when the settings file is unreadable.
            }

            _current = Normalize(new ManagerSettings());
        }
    }

    public void SetGitHubDnsOptimizationEnabled(bool enabled)
    {
        lock (_sync)
        {
            _current.GitHubDnsOptimizationEnabled = enabled;
            _current.LastSavedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe();
        }
    }

    public void SetGameResourceManifestSources(IEnumerable<GameResourceManifestSource> sources)
    {
        lock (_sync)
        {
            _current.GameResourceManifestSources = NormalizeSources(sources);
            _current.LastSavedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe();
        }
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(_settingsDirectory);
        var json = JsonSerializer.Serialize(_current, JsonOptions);
        File.WriteAllText(_settingsFile, json);
    }

    private static ManagerSettings Normalize(ManagerSettings settings)
    {
        settings.GameResourceManifestSources = NormalizeSources(settings.GameResourceManifestSources);
        return settings;
    }

    private static List<GameResourceManifestSource> NormalizeSources(IEnumerable<GameResourceManifestSource>? sources)
    {
        var result = (sources ?? Array.Empty<GameResourceManifestSource>())
            .Select(source => new GameResourceManifestSource
            {
                Order = source.Order,
                Url = NormalizeManifestSourceUrl(source.Url),
                Enabled = source.Enabled
            })
            .Where(source => !string.IsNullOrWhiteSpace(source.Url))
            .Select(CloneSource)
            .ToList();

        if (result.Count == 0)
        {
            result.Add(new GameResourceManifestSource
            {
                Url = DefaultManifestSourceUrl,
                Enabled = true
            });
        }

        for (var index = 0; index < result.Count; index++)
        {
            result[index].Order = index + 1;
            result[index].Url = result[index].Url.Trim();
        }

        return result;
    }

    private static GameResourceManifestSource CloneSource(GameResourceManifestSource source)
        => new()
        {
            Order = source.Order,
            Url = source.Url,
            Enabled = source.Enabled
        };

    private static string NormalizeManifestSourceUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length >= 5
                && (segments[2].Equals("blob", StringComparison.OrdinalIgnoreCase) || segments[2].Equals("raw", StringComparison.OrdinalIgnoreCase)))
            {
                var owner = segments[0];
                var repo = segments[1];
                var branch = segments[3];
                var path = string.Join('/', segments.Skip(4));
                return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
            }
        }

        if (uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.AbsolutePath.Replace("/blob/", "/raw/", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(path, uri.AbsolutePath, StringComparison.Ordinal))
            {
                var builder = new UriBuilder(uri) { Path = path };
                return builder.Uri.ToString();
            }
        }

        return trimmed;
    }
}
