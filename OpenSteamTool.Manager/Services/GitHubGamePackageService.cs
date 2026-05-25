using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubGamePackageService
{
    private const string RepositoryOwner = "G-Yoka";
    private const string RepositoryName = "GameResources";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GamePackageManifest? _cachedManifest;
    private DateTimeOffset _cachedAt;

    public string RepositoryUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}";

    public string ManifestUrl => $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/manifest.json";

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

            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
            using var response = await Http.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Manifest not found: {ManifestUrl}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to fetch manifest: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<GamePackageManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                throw new InvalidOperationException("Manifest content is empty or invalid.");
            }

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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSteamTool.Manager");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
