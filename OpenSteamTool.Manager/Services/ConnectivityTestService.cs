using System.Diagnostics;
using System.Net.Http;
using OpenSteamTool.Manager.Helpers;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class ConnectivityTestService
{
    private readonly GitHubHttpService github;
    private readonly CdnService cdn;

    public ConnectivityTestService(GitHubHttpService github, CdnService cdn)
    {
        this.github = github;
        this.cdn = cdn;
    }

    public async Task<IReadOnlyList<ConnectivityTestResult>> TestAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ConnectivityTestResult>
        {
            await TestEndpointAsync("jsDelivr CDN", "更新元数据", cdn.ManagerUpdateMetadataUrl, "application/json", includeGitHubHeaders: false, cancellationToken),
            await TestEndpointAsync("GitHub API", "更新发布列表", "https://api.github.com/repos/G-Yoka/OpenSteamTool-Manager/releases?per_page=1", "application/vnd.github+json", includeGitHubHeaders: true, cancellationToken),
            await TestEndpointAsync("jsDelivr CDN", "游戏资源清单", cdn.GameResourcesManifestUrl, "application/json", includeGitHubHeaders: false, cancellationToken),
            await TestEndpointAsync("GitHub Raw", "游戏资源清单", cdn.GameResourcesFallbackManifestUrl, "application/json", includeGitHubHeaders: false, cancellationToken)
        };

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
                ? "可访问"
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
}
