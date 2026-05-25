using System.IO;
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
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GamePackageManifest? _cachedManifest;
    private DateTimeOffset _cachedAt;

    public GitHubGamePackageService(GitHubHttpService github, CdnService cdn)
    {
        this.github = github;
        this.cdn = cdn;
    }

    public string RepositoryUrl => $"https://github.com/{CdnService.GameResourcesOwner}/{CdnService.GameResourcesRepository}";

    public string ManifestUrl => cdn.GameResourcesManifestUrl;

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
            var manifestResult = await LoadManifestFromUrlAsync(cdn.GameResourcesManifestUrl, includeGitHubHeaders: false, cancellationToken);
            if (manifestResult.Manifest is null && !string.IsNullOrWhiteSpace(manifestResult.Error))
            {
                errors.Add($"CDN: {manifestResult.Error}");
            }

            if (manifestResult.Manifest is null)
            {
                var fallbackResult = await LoadManifestFromUrlAsync(cdn.GameResourcesFallbackManifestUrl, includeGitHubHeaders: false, cancellationToken);
                if (fallbackResult.Manifest is null && !string.IsNullOrWhiteSpace(fallbackResult.Error))
                {
                    errors.Add($"GitHub: {fallbackResult.Error}");
                }

                manifestResult = fallbackResult;
            }

            var manifest = manifestResult.Manifest;
            if (manifest is null)
            {
                throw new InvalidOperationException(errors.Count == 0
                    ? $"Unable to load manifest from {ManifestUrl}."
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
                return (null, await github.BuildFailureMessageAsync(response, "Manifest request failed", cancellationToken, includeGitHubHeaders));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<GamePackageManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                return (null, "Manifest content is empty or invalid.");
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
        foreach (var item in manifest.Items)
        {
            item.OriginalZipUrl = item.ZipUrl?.Trim() ?? string.Empty;
            var resolved = cdn.ResolveGameResourceZipUrl(item.ZipUrl, item.ZipPath, out var mirrorUrl);
            item.ZipUrl = resolved;

            if (string.IsNullOrWhiteSpace(item.OriginalZipUrl) && !string.IsNullOrWhiteSpace(mirrorUrl))
            {
                item.OriginalZipUrl = mirrorUrl!;
            }
        }
    }
}
