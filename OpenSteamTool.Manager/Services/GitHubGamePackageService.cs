using System.Net.Http;
using System.Text.Json;
using OpenSteamTool.Manager.Helpers;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubGamePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubHttpService github;
    private readonly CdnService cdn;
    private readonly ManagerSettingsService settings;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GamePackageManifest? _cachedManifest;
    private DateTimeOffset _cachedAt;

    public GitHubGamePackageService(GitHubHttpService github, CdnService cdn, ManagerSettingsService settings)
    {
        this.github = github;
        this.cdn = cdn;
        this.settings = settings;
    }

    public string RepositoryUrl => $"https://github.com/{CdnService.GameResourcesOwner}/{CdnService.GameResourcesRepository}";

    public string ManifestUrl => settings.Current.NetworkSourcePriority == NetworkSourcePriority.GitHubFirst
        ? cdn.GameResourcesFallbackManifestUrl
        : cdn.GameResourcesManifestUrl;

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
            var manifestResult = await LoadManifestByPriorityAsync(errors, cancellationToken);
            var manifest = manifestResult.Manifest;
            if (manifest is null)
            {
                throw new InvalidOperationException(errors.Count == 0
                    ? $"无法从 {ManifestUrl} 读取游戏资源清单。"
                    : string.Join("；", errors));
            }

            NormalizePackages(manifest);
            manifest.Items ??= new List<GitHubGamePackage>();
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

    private async Task<(GamePackageManifest? Manifest, string? Error)> LoadManifestByPriorityAsync(
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var preferGitHub = settings.Current.NetworkSourcePriority == NetworkSourcePriority.GitHubFirst;
        var candidates = preferGitHub
            ? new[]
            {
                ("GitHub Raw", cdn.GameResourcesFallbackManifestUrl, false),
                ("jsDelivr CDN", cdn.GameResourcesManifestUrl, false)
            }
            : new[]
            {
                ("jsDelivr CDN", cdn.GameResourcesManifestUrl, false),
                ("GitHub Raw", cdn.GameResourcesFallbackManifestUrl, false)
            };

        foreach (var (name, url, includeGitHubHeaders) in candidates)
        {
            var result = await LoadManifestFromUrlAsync(url, includeGitHubHeaders, cancellationToken);
            if (result.Manifest is not null)
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                errors.Add($"{name}: {result.Error}");
            }
        }

        return (null, null);
    }

    private async Task<(GamePackageManifest? Manifest, string? Error)> LoadManifestFromUrlAsync(
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
                return (null, await github.BuildFailureMessageAsync(response, "资源清单请求失败", cancellationToken, includeGitHubHeaders));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<GamePackageManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                return (null, "资源清单为空或格式无效。");
            }

            manifest.Items ??= new List<GitHubGamePackage>();
            return (manifest, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }

    private void NormalizePackages(GamePackageManifest manifest)
    {
        var preferGitHub = settings.Current.NetworkSourcePriority == NetworkSourcePriority.GitHubFirst;
        foreach (var item in manifest.Items)
        {
            var originalUrl = item.ZipUrl?.Trim() ?? string.Empty;
            var cdnUrl = cdn.ResolveGameResourceZipUrl(item.ZipUrl, item.ZipPath, out var mirrorUrl);

            if (preferGitHub && !string.IsNullOrWhiteSpace(originalUrl))
            {
                item.ZipUrl = originalUrl;
                item.OriginalZipUrl = !string.Equals(originalUrl, cdnUrl, StringComparison.OrdinalIgnoreCase)
                    ? cdnUrl
                    : mirrorUrl ?? string.Empty;
                continue;
            }

            if (preferGitHub && !string.IsNullOrWhiteSpace(item.ZipPath))
            {
                item.ZipUrl = cdn.BuildGameResourcesRawUrl(item.ZipPath);
                item.OriginalZipUrl = cdnUrl;
                continue;
            }

            item.ZipUrl = cdnUrl;
            item.OriginalZipUrl = originalUrl;

            if (string.IsNullOrWhiteSpace(item.OriginalZipUrl) && !string.IsNullOrWhiteSpace(mirrorUrl))
            {
                item.OriginalZipUrl = mirrorUrl;
            }
        }
    }
}
