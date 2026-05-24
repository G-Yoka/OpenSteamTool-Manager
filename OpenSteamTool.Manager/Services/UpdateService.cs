using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CreateClient();

    private readonly string _owner;
    private readonly string _repository;

    public UpdateService(string owner, string repository)
    {
        _owner = owner;
        _repository = repository;
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersionText();
        var requestUri = $"https://api.github.com/repos/{_owner}/{_repository}/releases?per_page=100";

        try
        {
            using var response = await Http.GetAsync(requestUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return BuildNoRelease(currentVersion, "仓库暂无公开 Release，或 Release 入口尚未建立。");
                }

                return BuildFailure(currentVersion, $"GitHub Releases API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}。{ExtractErrorMessage(payload)}");
            }

            var releases = JsonSerializer.Deserialize<List<GitHubReleaseDto>>(payload, JsonOptions);
            var release = releases?.FirstOrDefault(x => x is not null && !x.Draft && !x.Prerelease && !string.IsNullOrWhiteSpace(x.TagName));
            if (release is null)
            {
                return BuildNoRelease(currentVersion, "仓库没有可用的公开 Release。");
            }

            var tagName = release.TagName;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return BuildNoRelease(currentVersion, "仓库没有可用的公开 Release。");
            }

            if (!TryParseVersionTag(tagName, out var latestVersion))
            {
                return BuildFailure(currentVersion, $"无法解析最新 Release 标签：{tagName}");
            }

            var current = Version.Parse(currentVersion);
            var isUpdateAvailable = latestVersion > current;
            var latestVersionText = latestVersion.ToString(3);

            return new UpdateCheckResult
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersionText,
                ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? tagName : release.Name,
                ReleaseUrl = release.HtmlUrl ?? $"https://github.com/{_owner}/{_repository}/releases/latest",
                PublishedAt = release.PublishedAt,
                IsUpdateAvailable = isUpdateAvailable,
                HasRelease = true
            };
        }
        catch (Exception ex)
        {
            return BuildFailure(currentVersion, $"检查更新失败：{ex.Message}");
        }
    }

    public static string GetCurrentVersionText()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSteamTool-Manager");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool TryParseVersionTag(string tagName, out Version version)
    {
        version = new Version(0, 0);
        var normalized = tagName.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (Version.TryParse(normalized, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        return false;
    }

    private static UpdateCheckResult BuildFailure(string currentVersion, string message)
        => new()
        {
            CurrentVersion = currentVersion,
            ErrorMessage = message
        };

    private static UpdateCheckResult BuildNoRelease(string currentVersion, string message)
        => new()
        {
            CurrentVersion = currentVersion,
            HasRelease = false,
            ErrorMessage = message
        };

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<GitHubErrorDto>(payload, JsonOptions);
            return string.IsNullOrWhiteSpace(error?.Message) ? string.Empty : $"错误信息：{error.Message}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }

    private sealed class GitHubErrorDto
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}