using System.Net.Http;
using System.Text.Json;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubGamePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubHttpService github;
    private readonly ManagerSettingsService settings;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GamePackageManifest? _cachedManifest;
    private DateTimeOffset _cachedAt;

    public GitHubGamePackageService(GitHubHttpService github, ManagerSettingsService settings)
    {
        this.github = github;
        this.settings = settings;
    }

    public string RepositoryUrl => "https://github.com/G-Yoka/GameResources";

    public string ManifestUrl
        => string.Join("; ", settings.Current.GameResourceManifestSources
            .Where(source => source.Enabled)
            .OrderBy(source => source.Order)
            .Select(source => source.Url));

    public async Task<GamePackageManifest> LoadManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedManifest is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(10))
        {
            return _cachedManifest;
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedManifest is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(10))
            {
                return _cachedManifest;
            }

            var errors = new List<string>();
            var manifests = await LoadManifestsBySettingsAsync(errors, cancellationToken);
            if (manifests.Count == 0)
            {
                throw new InvalidOperationException(errors.Count == 0
                    ? $"无法读取游戏资源清单：{ManifestUrl}"
                    : string.Join("；", errors));
            }

            var manifest = new GamePackageManifest();
            foreach (var loaded in manifests)
            {
                foreach (var item in loaded.Manifest.Items)
                {
                    NormalizePackage(item, loaded.SourceUrl);
                    manifest.Items.Add(item);
                }
            }

            _cachedManifest = manifest;
            _cachedAt = DateTimeOffset.UtcNow;
            return manifest;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<GitHubGamePackage>> LoadPackagesForAppIdAsync(
        string appId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(forceRefresh, cancellationToken);
        return manifest.Items
            .Where(item => item.Enabled && string.Equals(item.AppId, appId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ClearCache()
    {
        _cachedManifest = null;
        _cachedAt = default;
    }

    private async Task<List<(GamePackageManifest Manifest, string SourceUrl)>> LoadManifestsBySettingsAsync(
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var result = new List<(GamePackageManifest Manifest, string SourceUrl)>();
        var sources = settings.Current.GameResourceManifestSources
            .Where(source => source.Enabled && !string.IsNullOrWhiteSpace(source.Url))
            .OrderBy(source => source.Order)
            .ToList();

        if (sources.Count == 0)
        {
            errors.Add("未启用任何游戏资源清单源。");
            return result;
        }

        foreach (var source in sources)
        {
            var candidates = BuildSourceCandidates(source.Url);
            if (candidates.Count == 0)
            {
                errors.Add($"清单源地址无效：{source.Url}");
                continue;
            }

            foreach (var candidate in candidates)
            {
                var manifestResult = await LoadManifestFromUrlAsync(candidate.Name, candidate.Url, candidate.IncludeGitHubHeaders, cancellationToken);
                if (manifestResult.Manifest is not null)
                {
                    result.Add((manifestResult.Manifest, candidate.Url));
                    break;
                }

                if (!string.IsNullOrWhiteSpace(manifestResult.Error))
                {
                    errors.Add(manifestResult.Error);
                }
            }
        }

        return result;
    }

    private async Task<(GamePackageManifest? Manifest, string? Error)> LoadManifestFromUrlAsync(
        string sourceName,
        string url,
        bool includeGitHubHeaders,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = github.CreateRequest(HttpMethod.Get, url, "application/json", includeGitHubHeaders);
            using var response = await github.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = await github.BuildFailureMessageAsync(response, "资源清单请求失败", cancellationToken, includeGitHubHeaders);
                return (null, $"{sourceName}: {message}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<GamePackageManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                return (null, $"{sourceName}: 资源清单为空或格式无效。");
            }

            manifest.Items ??= new List<GitHubGamePackage>();
            return (manifest, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"{sourceName}: {ex.Message}");
        }
    }

    private static IReadOnlyList<(string Name, string Url, bool IncludeGitHubHeaders)> BuildSourceCandidates(string url)
    {
        var normalized = NormalizeManifestSourceUrl(url);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<(string, string, bool)>();
        }

        var includeGitHubHeaders = Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
        var name = includeGitHubHeaders ? "GitHub API" : "资源清单";
        return new[] { (name, normalized, includeGitHubHeaders) };
    }

    private void NormalizePackage(GitHubGamePackage item, string sourceUrl)
    {
        var originalUrl = item.ZipUrl?.Trim() ?? string.Empty;
        var normalizedZipUrl = NormalizeDownloadUrl(originalUrl);
        var sourceRelativeUrl = ResolveRelativeUrl(sourceUrl, item.ZipPath);

        if (!string.IsNullOrWhiteSpace(normalizedZipUrl))
        {
            item.ZipUrl = normalizedZipUrl;
            item.OriginalZipUrl = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(sourceRelativeUrl))
        {
            item.ZipUrl = sourceRelativeUrl;
            item.OriginalZipUrl = string.Empty;
            return;
        }

        item.ZipUrl = string.Empty;
        item.OriginalZipUrl = string.Empty;
    }

    private static string ResolveRelativeUrl(string sourceUrl, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(zipPath, UriKind.Absolute, out var absolute))
        {
            return NormalizeDownloadUrl(absolute.ToString());
        }

        if (!Uri.TryCreate(NormalizeManifestSourceUrl(sourceUrl), UriKind.Absolute, out var sourceUri))
        {
            return string.Empty;
        }

        return new Uri(sourceUri, zipPath.Replace('\\', '/')).ToString();
    }

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

    private static string NormalizeDownloadUrl(string url)
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
