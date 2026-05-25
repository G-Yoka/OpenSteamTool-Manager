namespace OpenSteamTool.Manager.Helpers;

public sealed class CdnService
{
    public const string ManagerOwner = "G-Yoka";
    public const string ManagerRepository = "OpenSteamTool-Manager";
    public const string GameResourcesOwner = "G-Yoka";
    public const string GameResourcesRepository = "GameResources";
    public const string DefaultBranch = "main";

    public string ManagerRepositoryUrl => $"https://github.com/{ManagerOwner}/{ManagerRepository}";

    public string ManagerReleasesUrl => $"{ManagerRepositoryUrl}/releases";

    public string ManagerUpdateMetadataUrl => BuildManagerCdnUrl("cdn/update.json");

    public string GameResourcesManifestUrl => BuildGameResourcesCdnUrl("manifest.json");

    public string GameResourcesFallbackManifestUrl => $"https://raw.githubusercontent.com/{GameResourcesOwner}/{GameResourcesRepository}/{DefaultBranch}/manifest.json";

    public string BuildManagerCdnUrl(string relativePath)
        => BuildCdnUrl(ManagerOwner, ManagerRepository, relativePath);

    public string BuildGameResourcesCdnUrl(string relativePath)
        => BuildCdnUrl(GameResourcesOwner, GameResourcesRepository, relativePath);

    public string ResolveGameResourceZipUrl(string? zipUrl, string? zipPath, out string? mirrorUrl)
    {
        mirrorUrl = null;

        if (!string.IsNullOrWhiteSpace(zipUrl))
        {
            var trimmed = zipUrl.Trim();
            if (TryRewriteToCdn(trimmed, out var cdnUrl))
            {
                mirrorUrl = trimmed;
                return cdnUrl;
            }

            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(zipPath))
        {
            return BuildGameResourcesCdnUrl(zipPath);
        }

        return string.Empty;
    }

    public string ResolveManagerAssetUrl(string? assetPath, string? assetUrl, out string? mirrorUrl)
    {
        mirrorUrl = null;

        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            var cdnUrl = BuildManagerCdnUrl(assetPath);
            if (!string.IsNullOrWhiteSpace(assetUrl))
            {
                mirrorUrl = assetUrl.Trim();
            }

            return cdnUrl;
        }

        if (!string.IsNullOrWhiteSpace(assetUrl))
        {
            return assetUrl.Trim();
        }

        return string.Empty;
    }

    public bool TryRewriteToCdn(string url, out string cdnUrl)
    {
        cdnUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Equals("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase))
        {
            cdnUrl = url;
            return true;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 4)
        {
            var owner = segments[0];
            var repository = segments[1];
            var branch = segments[2];
            var path = string.Join('/', segments.Skip(3));
            cdnUrl = BuildCdnUrl(owner, repository, branch, path);
            return true;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 5)
        {
            var owner = segments[0];
            var repository = segments[1];
            var mode = segments[2];
            if ((mode.Equals("raw", StringComparison.OrdinalIgnoreCase) || mode.Equals("blob", StringComparison.OrdinalIgnoreCase))
                && segments.Length >= 5)
            {
                var branch = segments[3];
                var path = string.Join('/', segments.Skip(4));
                cdnUrl = BuildCdnUrl(owner, repository, branch, path);
                return true;
            }
        }

        return false;
    }

    private static string BuildCdnUrl(string owner, string repository, string relativePath)
        => BuildCdnUrl(owner, repository, DefaultBranch, relativePath);

    private static string BuildCdnUrl(string owner, string repository, string branch, string relativePath)
    {
        var cleanPath = relativePath.Replace('\\', '/').TrimStart('/');
        return $"https://cdn.jsdelivr.net/gh/{owner}/{repository}@{branch}/{cleanPath}";
    }
}
