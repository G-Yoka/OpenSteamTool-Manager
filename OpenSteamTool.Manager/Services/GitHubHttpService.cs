using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubHttpService
{
    private readonly HttpClient http;
    private readonly GitHubTokenService tokenService;
    private readonly GitHubDnsService dnsService;

    public GitHubHttpService(GitHubTokenService tokenService, GitHubDnsService dnsService)
    {
        this.tokenService = tokenService;
        this.dnsService = dnsService;
        http = new HttpClient(CreateHandler(dnsService))
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
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
        => http.SendAsync(request, cancellationToken);

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default)
        => http.SendAsync(request, completionOption, cancellationToken);

    public async Task<string> BuildFailureMessageAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken = default,
        bool includeRateLimitDetails = true)
    {
        var message = $"{context}: {(int)response.StatusCode} {response.ReasonPhrase}";
        var body = await TryReadBodyAsync(response, cancellationToken);

        if (includeRateLimitDetails
            && response.StatusCode == HttpStatusCode.Forbidden
            && IsRateLimited(response))
        {
            var resetText = FormatResetTime(response);
            message = string.IsNullOrWhiteSpace(resetText)
                ? "GitHub API 已限流，请配置 PAT 或稍后重试。"
                : $"GitHub API 已限流，请配置 PAT 或稍后重试。重置时间：{resetText}";
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            message += $"；错误信息：{body.Trim()}";
        }

        return message;
    }

    private static SocketsHttpHandler CreateHandler(GitHubDnsService dnsService)
        => new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                var addresses = await ResolveAddressesAsync(dnsService, host, cancellationToken);
                Exception? lastError = null;

                foreach (var address in addresses)
                {
                    try
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(address, port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                throw new HttpRequestException($"无法连接到 {host}:{port}", lastError);
            }
        };

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveAddressesAsync(
        GitHubDnsService dnsService,
        string host,
        CancellationToken cancellationToken)
    {
        if (dnsService.IsGitHubHost(host))
        {
            var optimized = await dnsService.ResolveAsync(host, cancellationToken);
            if (optimized.Count > 0)
            {
                return optimized;
            }
        }

        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
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
