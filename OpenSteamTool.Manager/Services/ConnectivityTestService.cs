using System.Diagnostics;
using System.Net.Http;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class ConnectivityTestService
{
    private readonly GitHubDnsService dns;
    private readonly GitHubHttpService github;
    private readonly ManagerSettingsService settings;

    public ConnectivityTestService(GitHubDnsService dns, GitHubHttpService github, ManagerSettingsService settings)
    {
        this.dns = dns;
        this.github = github;
        this.settings = settings;
    }

    public async Task<IReadOnlyList<ConnectivityTestResult>> TestAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ConnectivityTestResult>
        {
            new()
            {
                Source = "GitHub DNS",
                Purpose = "优化开关",
                Status = dns.IsEnabled ? "已启用" : "已关闭",
                DurationMs = 0,
                Detail = dns.StateText
            }
        };

        foreach (var host in dns.Hosts)
        {
            results.Add(await dns.TestResolveAsync(host, cancellationToken));
        }

        results.Add(await TestEndpointAsync(
            "GitHub API",
            "更新发布列表",
            "https://api.github.com/repos/G-Yoka/OpenSteamTool-Manager/releases?per_page=1",
            "application/vnd.github+json",
            includeGitHubHeaders: true,
            cancellationToken));

        results.AddRange(await TestManifestSourcesAsync(cancellationToken));
        return results;
    }

    private async Task<IReadOnlyList<ConnectivityTestResult>> TestManifestSourcesAsync(CancellationToken cancellationToken)
    {
        var results = new List<ConnectivityTestResult>();
        foreach (var source in settings.Current.GameResourceManifestSources
                     .Where(source => source.Enabled && !string.IsNullOrWhiteSpace(source.Url))
                     .OrderBy(source => source.Order))
        {
            results.Add(await TestEndpointAsync(
                "资源清单源",
                $"第 {source.Order} 个清单",
                source.Url,
                "application/json",
                IsGitHubApiUrl(source.Url),
                cancellationToken));
        }

        return results;
    }

    private async Task<ConnectivityTestResult> TestEndpointAsync(
        string source,
        string purpose,
        string url,
        string accept,
        bool includeGitHubHeaders,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var request = github.CreateRequest(HttpMethod.Get, url, accept, includeGitHubHeaders);
            using var response = await github.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            var status = response.IsSuccessStatusCode
                ? BuildSuccessStatus(response, accept)
                : await github.BuildFailureMessageAsync(response, "连通性测试失败", timeout.Token);

            return new ConnectivityTestResult
            {
                Source = source,
                Purpose = purpose,
                Status = status,
                DurationMs = (int)Math.Max(stopwatch.ElapsedMilliseconds, 1),
                Detail = url
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectivityTestResult
            {
                Source = source,
                Purpose = purpose,
                Status = "超时",
                DurationMs = (int)Math.Max(stopwatch.ElapsedMilliseconds, 1),
                Detail = url
            };
        }
        catch (Exception ex)
        {
            return new ConnectivityTestResult
            {
                Source = source,
                Purpose = purpose,
                Status = "失败",
                DurationMs = (int)Math.Max(stopwatch.ElapsedMilliseconds, 1),
                Detail = $"{url} - {ex.Message}"
            };
        }
    }

    private static bool IsGitHubApiUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static string BuildSuccessStatus(HttpResponseMessage response, string accept)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (accept.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return "可访问，但返回内容不是 JSON";
        }

        return "可访问";
    }
}
