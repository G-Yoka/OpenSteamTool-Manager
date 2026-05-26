using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSteamTool.Manager.Helpers;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubHttpService github;
    private readonly CdnService cdn;
    private readonly ManagerSettingsService settings;
    private readonly Version currentVersion;

    public UpdateService(GitHubHttpService github, CdnService cdn, ManagerSettingsService settings)
    {
        this.github = github;
        this.cdn = cdn;
        this.settings = settings;
        currentVersion = ReadCurrentVersion();
    }

    public string CurrentVersionText => currentVersion.ToString(3);

    public string RepositoryUrl => cdn.ManagerRepositoryUrl;

    public string ReleasesUrl => cdn.ManagerReleasesUrl;

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        if (settings.Current.NetworkSourcePriority == NetworkSourcePriority.GitHubFirst)
        {
            var githubResult = await CheckLatestFromGitHubAsync(cancellationToken);
            if (githubResult.HasRelease && githubResult.ErrorMessage is null)
            {
                return await AddCdnMirrorIfAvailableAsync(githubResult, cancellationToken);
            }

            return await TryLoadLatestFromCdnAsync(cancellationToken) ?? githubResult;
        }

        var cdnResult = await TryLoadLatestFromCdnAsync(cancellationToken);
        return cdnResult ?? await CheckLatestFromGitHubAsync(cancellationToken);
    }

    public async Task StartSelfUpdateAsync(
        UpdateCheckResult release,
        string installDirectory,
        int processId,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!release.HasRelease)
        {
            throw new InvalidOperationException("没有可用于自动更新的公开 Release。");
        }

        if (string.IsNullOrWhiteSpace(release.AssetDownloadUrl))
        {
            throw new InvalidOperationException("该 Release 没有可下载的 ZIP 发布包。");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "OpenSteamTool.Manager", "update", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        var downloadDir = Path.Combine(workDir, "download");
        var extractDir = Path.Combine(workDir, "extract");
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(extractDir);

        var zipName = string.IsNullOrWhiteSpace(release.AssetName) ? "OpenSteamTool.Manager-update.zip" : release.AssetName;
        var zipPath = Path.Combine(downloadDir, zipName);
        var sourceRoot = await DownloadAssetWithFallbackAsync(release.AssetDownloadUrl, release.AssetMirrorUrl, zipPath, progress, cancellationToken);

        progress?.Report(new UpdateProgress { Stage = "解压中", Percent = 0, Message = "正在准备更新文件..." });
        ZipFile.ExtractToDirectory(sourceRoot, extractDir, overwriteFiles: true);

        var payloadRoot = ResolvePayloadRoot(extractDir);
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "OpenSteamTool.Manager.exe");
        var updateScript = Path.Combine(workDir, "apply-update.ps1");

        await File.WriteAllTextAsync(updateScript, BuildScript(), new UTF8Encoding(false), cancellationToken);
        StartHelperProcess(updateScript, payloadRoot, installDirectory, exeName, processId);
        progress?.Report(new UpdateProgress { Stage = "替换中", Percent = 100, Message = "更新辅助进程已启动，主程序退出后将自动替换并重启。" });
    }

    private async Task<UpdateCheckResult?> TryLoadLatestFromCdnAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = github.CreateRequest(HttpMethod.Get, cdn.ManagerUpdateMetadataUrl, "application/json", includeGitHubHeaders: false);
            using var response = await github.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var metadata = await JsonSerializer.DeserializeAsync<CdnUpdateMetadata>(stream, JsonOptions, cancellationToken);
            return metadata is null ? null : BuildResultFromMetadata(metadata);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<UpdateCheckResult> AddCdnMirrorIfAvailableAsync(UpdateCheckResult result, CancellationToken cancellationToken)
    {
        var cdnResult = await TryLoadLatestFromCdnAsync(cancellationToken);
        if (cdnResult is null
            || cdnResult.LatestVersion is null
            || result.LatestVersion is null
            || cdnResult.LatestVersion != result.LatestVersion
            || string.IsNullOrWhiteSpace(cdnResult.AssetDownloadUrl))
        {
            return result;
        }

        return new UpdateCheckResult
        {
            CurrentVersion = result.CurrentVersion,
            LatestVersion = result.LatestVersion,
            ReleaseName = result.ReleaseName,
            ReleaseTag = result.ReleaseTag,
            PublishedAt = result.PublishedAt,
            ReleasePageUrl = result.ReleasePageUrl,
            AssetName = result.AssetName,
            AssetDownloadUrl = result.AssetDownloadUrl,
            AssetMirrorUrl = cdnResult.AssetDownloadUrl,
            HasRelease = result.HasRelease,
            IsUpdateAvailable = result.IsUpdateAvailable,
            ErrorMessage = result.ErrorMessage
        };
    }

    private UpdateCheckResult? BuildResultFromMetadata(CdnUpdateMetadata metadata)
    {
        var versionText = FirstNonEmpty(metadata.VersionTag, metadata.ReleaseTag);
        var latestVersion = ParseVersion(versionText);
        if (latestVersion is null)
        {
            return null;
        }

        var releaseName = FirstNonEmpty(metadata.ReleaseName, metadata.ReleaseTag, metadata.VersionTag);
        var releaseTag = FirstNonEmpty(metadata.ReleaseTag, metadata.VersionTag);
        var releasePageUrl = FirstNonEmpty(metadata.ReleasePageUrl, ReleasesUrl);
        var assetDownloadUrl = cdn.ResolveManagerAssetUrl(metadata.AssetPath, metadata.AssetUrl, out var mirrorUrl);
        var assetName = FirstNonEmpty(Path.GetFileName(metadata.AssetPath ?? string.Empty), Path.GetFileName(new UriSafe(assetDownloadUrl).LocalPath), $"{releaseTag}.zip");

        if (latestVersion > currentVersion && string.IsNullOrWhiteSpace(assetDownloadUrl))
        {
            return null;
        }

        return new UpdateCheckResult
        {
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseName = releaseName,
            ReleaseTag = releaseTag,
            PublishedAt = metadata.PublishedAt,
            ReleasePageUrl = releasePageUrl,
            AssetName = assetName,
            AssetDownloadUrl = assetDownloadUrl,
            AssetMirrorUrl = mirrorUrl ?? string.Empty,
            HasRelease = true,
            IsUpdateAvailable = latestVersion > currentVersion
        };
    }

    private async Task<UpdateCheckResult> CheckLatestFromGitHubAsync(CancellationToken cancellationToken)
    {
        const string apiUrl = "https://api.github.com/repos/G-Yoka/OpenSteamTool-Manager/releases?per_page=1";

        try
        {
            using var request = github.CreateRequest(HttpMethod.Get, apiUrl);
            using var response = await github.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return CreateNoReleaseResult();
            }

            if (!response.IsSuccessStatusCode)
            {
                return CreateFailureResult(await github.BuildFailureMessageAsync(response, "GitHub Releases API 请求失败", cancellationToken));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<GitHubReleaseDto[]>(stream, JsonOptions, cancellationToken)
                ?? Array.Empty<GitHubReleaseDto>();

            var release = releases.FirstOrDefault(x => !x.Draft && !x.Prerelease);
            if (release is null)
            {
                return CreateNoReleaseResult();
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion is null)
            {
                return new UpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    HasRelease = true,
                    ReleaseName = release.Name ?? release.TagName ?? string.Empty,
                    ReleaseTag = release.TagName ?? string.Empty,
                    PublishedAt = release.PublishedAt,
                    ReleasePageUrl = release.HtmlUrl ?? ReleasesUrl,
                    ErrorMessage = $"Release 标签 {release.TagName} 不是有效的 SemVer 版本号。"
                };
            }

            var asset = FindUpdateAsset(release.Assets);

            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseName = release.Name ?? release.TagName ?? string.Empty,
                ReleaseTag = release.TagName ?? string.Empty,
                PublishedAt = release.PublishedAt,
                ReleasePageUrl = release.HtmlUrl ?? ReleasesUrl,
                AssetName = asset?.Name ?? string.Empty,
                AssetDownloadUrl = asset?.BrowserDownloadUrl ?? string.Empty,
                HasRelease = true,
                IsUpdateAvailable = latestVersion > currentVersion
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateFailureResult($"GitHub Releases API 请求失败: {ex.Message}");
        }
    }

    private UpdateCheckResult CreateNoReleaseResult()
        => new()
        {
            CurrentVersion = currentVersion,
            HasRelease = false
        };

    private UpdateCheckResult CreateFailureResult(string message)
        => new()
        {
            CurrentVersion = currentVersion,
            HasRelease = false,
            ErrorMessage = message
        };

    private static Version? ParseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var trimmed = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var version) ? Normalize(version) : null;
    }

    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static GitHubReleaseAssetDto? FindUpdateAsset(IReadOnlyList<GitHubReleaseAssetDto> assets)
    {
        if (assets.Count == 0)
        {
            return null;
        }

        return assets.FirstOrDefault(x =>
                   x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && x.Name.Contains("OpenSteamTool.Manager", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> DownloadAssetWithFallbackAsync(
        string primaryUrl,
        string? mirrorUrl,
        string zipPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryUrl))
        {
            candidates.Add(primaryUrl.Trim());
        }

        if (!string.IsNullOrWhiteSpace(mirrorUrl) && !candidates.Contains(mirrorUrl.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(mirrorUrl.Trim());
        }

        Exception? lastFailure = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var url = candidates[index];
            try
            {
                var includeGitHubHeaders = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
                using var request = github.CreateRequest(HttpMethod.Get, url, "application/octet-stream", includeGitHubHeaders);
                using var response = await github.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var message = await github.BuildFailureMessageAsync(response, "更新包下载失败", cancellationToken, includeRateLimitDetails: false);
                    throw new InvalidOperationException(message);
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(zipPath);

                var totalLength = response.Content.Headers.ContentLength;
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    readTotal += read;

                    if (totalLength is > 0)
                    {
                        progress?.Report(new UpdateProgress
                        {
                            Stage = "下载中",
                            Percent = (int)Math.Clamp(readTotal * 100L / totalLength.Value, 0, 100),
                            Message = "正在下载更新包..."
                        });
                    }
                }

                progress?.Report(new UpdateProgress
                {
                    Stage = "下载中",
                    Percent = 100,
                    Message = "更新包下载完成。"
                });

                return zipPath;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                if (index < candidates.Count - 1)
                {
                    progress?.Report(new UpdateProgress
                    {
                        Stage = "下载中",
                        Percent = 0,
                        Message = "首选下载源失败，正在尝试备用地址..."
                    });
                }
            }
        }

        throw new InvalidOperationException(lastFailure?.Message ?? "更新包下载失败。");
    }

    private static string ResolvePayloadRoot(string extractDir)
    {
        if (File.Exists(Path.Combine(extractDir, "OpenSteamTool.Manager.exe")))
        {
            return extractDir;
        }

        var directories = Directory.EnumerateDirectories(extractDir).ToList();
        if (directories.Count == 1)
        {
            var nested = directories[0];
            if (File.Exists(Path.Combine(nested, "OpenSteamTool.Manager.exe")))
            {
                return nested;
            }
        }

        return extractDir;
    }

    private static void StartHelperProcess(string scriptPath, string sourceDir, string targetDir, string exeName, int processId)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
        {
            powershell = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Source");
        startInfo.ArgumentList.Add(sourceDir);
        startInfo.ArgumentList.Add("-Target");
        startInfo.ArgumentList.Add(targetDir);
        startInfo.ArgumentList.Add("-ExeName");
        startInfo.ArgumentList.Add(exeName);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(processId.ToString());

        using var helper = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新辅助进程。");
    }

    private static Version ReadCurrentVersion()
    {
        var entry = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var trimmed = informational.Trim().TrimStart('v', 'V');
            if (Version.TryParse(trimmed, out var version))
            {
                return Normalize(version);
            }
        }

        var versionInfo = entry.GetName().Version ?? new Version(0, 0, 0, 0);
        return Normalize(versionInfo);
    }

    private static string BuildScript()
        => """
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$ExeName,
    [Parameter(Mandatory = $true)][int]$ProcessId
)

$ErrorActionPreference = 'Stop'

while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path -LiteralPath $Source)) {
    throw "更新源目录不存在：$Source"
}

New-Item -ItemType Directory -Path $Target -Force | Out-Null

& "$env:SystemRoot\System32\robocopy.exe" $Source $Target /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Robocopy 复制失败，退出码：$LASTEXITCODE"
}

$exePath = Join-Path $Target $ExeName
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "更新后的可执行文件不存在：$exePath"
}

Start-Process -FilePath $exePath -WorkingDirectory $Target | Out-Null
""";

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDto> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed class UriSafe
    {
        public UriSafe(string value)
        {
            Value = !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? uri
                : new Uri("https://example.invalid/");
        }

        public Uri Value { get; }

        public string LocalPath => Value.LocalPath;
    }
}
