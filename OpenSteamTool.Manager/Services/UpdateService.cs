using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubHttpService github;
    private readonly Version currentVersion;

    public UpdateService(GitHubHttpService github)
    {
        this.github = github;
        currentVersion = ReadCurrentVersion();
    }

    public string CurrentVersionText => currentVersion.ToString(3);

    public string RepositoryUrl => "https://github.com/G-Yoka/OpenSteamTool-Manager";

    public string ReleasesUrl => $"{RepositoryUrl}/releases";

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
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
        await DownloadAssetAsync(release.AssetDownloadUrl, zipPath, progress, cancellationToken);

        progress?.Report(new UpdateProgress { Stage = "解压中", Percent = 0, Message = "正在准备更新文件..." });
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var payloadRoot = ResolvePayloadRoot(extractDir);
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "OpenSteamTool.Manager.exe");
        var updateScript = Path.Combine(workDir, "apply-update.ps1");

        await File.WriteAllTextAsync(updateScript, BuildScript(), new UTF8Encoding(false), cancellationToken);
        StartHelperProcess(updateScript, payloadRoot, installDirectory, exeName, processId);
        progress?.Report(new UpdateProgress { Stage = "替换中", Percent = 100, Message = "更新辅助进程已启动，主程序退出后将自动替换并重启。" });
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

    private async Task DownloadAssetAsync(
        string url,
        string zipPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress { Stage = "下载中", Percent = 0, Message = "正在下载更新包..." });

        using var request = github.CreateRequest(HttpMethod.Get, url, "application/octet-stream", includeGitHubHeaders: false);
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
                var percent = (int)Math.Clamp(readTotal * 100L / totalLength.Value, 0, 100);
                progress?.Report(new UpdateProgress
                {
                    Stage = "下载中",
                    Percent = percent,
                    Message = "正在下载更新包..."
                });
            }
        }
    }

    private Version ReadCurrentVersion()
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
}
