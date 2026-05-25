using System.Net.Http;
using System.Net.Http.Headers;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubHttpService
{
    private static readonly HttpClient Http = new();
    private readonly GitHubTokenService tokenService;

    public GitHubHttpService(GitHubTokenService tokenService)
    {
        this.tokenService = tokenService;
    }

    public HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string accept = "application/vnd.github+json",
        bool includeGitHubHeaders = true)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("OpenSteamTool.Manager");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        if (includeGitHubHeaders)
        {
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            var token = tokenService.GetToken();
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return request;
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        => Http.SendAsync(request, cancellationToken);

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default)
        => Http.SendAsync(request, completionOption, cancellationToken);

    public async Task<string> BuildFailureMessageAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken = default,
        bool includeRateLimitDetails = true)
    {
        var message = $"{context}: {(int)response.StatusCode} {response.ReasonPhrase}";
        var body = await TryReadBodyAsync(response, cancellationToken);

        if (includeRateLimitDetails
            && response.StatusCode == System.Net.HttpStatusCode.Forbidden
            && IsRateLimited(response))
        {
            var resetText = FormatResetTime(response);
            message = string.IsNullOrWhiteSpace(resetText)
                ? "GitHub API 已限流，请配置 PAT 或稍后重试。"
                : $"GitHub API 已限流，请配置 PAT 或稍后重试。重置时间：{resetText}";
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            message += $" 错误信息: {body}";
        }

        return message;
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
            && values.FirstOrDefault() == "0")
        {
            return true;
        }

        return response.ReasonPhrase?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string FormatResetTime(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            || !long.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return string.Empty;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static async Task<string> TryReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }
}
