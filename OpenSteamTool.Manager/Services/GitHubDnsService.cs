using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GitHubDnsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly HttpClient DohHttp = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private static readonly string[] DoHEndpoints =
    {
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/resolve"
    };

    private static readonly string[] GitHubHosts =
    {
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "codeload.github.com",
        "objects.githubusercontent.com",
        "githubusercontent.com"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ManagerSettingsService settings;
    private readonly ConcurrentDictionary<string, CachedResolution> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim cacheLock = new(1, 1);

    public GitHubDnsService(ManagerSettingsService settings)
    {
        this.settings = settings;
    }

    public bool IsEnabled => settings.Current.GitHubDnsOptimizationEnabled;

    public IReadOnlyList<string> Hosts => GitHubHosts;

    public string StateText => IsEnabled
        ? "GitHub DNS 优化：已启用，仅对 GitHub 相关域名使用 DoH 解析。"
        : "GitHub DNS 优化：已关闭，GitHub 请求将直接使用系统 DNS。";

    public bool IsGitHubHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return GitHubHosts.Any(candidate =>
            host.Equals(candidate, StringComparison.OrdinalIgnoreCase)
            || (candidate.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                && host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)));
    }

    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Array.Empty<IPAddress>();
        }

        if (!IsEnabled || !IsGitHubHost(host))
        {
            return await ResolveSystemDnsAsync(host, cancellationToken);
        }

        if (TryGetCached(host, out var cached) && cached is not null)
        {
            return cached.Addresses;
        }

        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(host, out cached) && cached is not null)
            {
                return cached.Addresses;
            }

            var addresses = await ResolveWithDoHAsync(host, cancellationToken);
            if (addresses.Count == 0)
            {
                addresses = await ResolveSystemDnsAsync(host, cancellationToken);
            }

            cache[host] = new CachedResolution(addresses, DateTimeOffset.UtcNow.Add(CacheDuration));
            return addresses;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<ConnectivityTestResult> TestResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var addresses = await ResolveAsync(host, cancellationToken);
            var addressesText = addresses.Count == 0
                ? "未解析到地址"
                : string.Join(", ", addresses.Take(4).Select(address => address.ToString()));

            return new ConnectivityTestResult
            {
                Source = "GitHub DNS",
                Purpose = $"解析 {host}",
                Status = addresses.Count > 0 ? "可解析" : "未解析到地址",
                DurationMs = (int)Math.Max(stopwatch.ElapsedMilliseconds, 1),
                Detail = addressesText
            };
        }
        catch (Exception ex)
        {
            return new ConnectivityTestResult
            {
                Source = "GitHub DNS",
                Purpose = $"解析 {host}",
                Status = "失败",
                DurationMs = (int)Math.Max(stopwatch.ElapsedMilliseconds, 1),
                Detail = ex.Message
            };
        }
    }

    private bool TryGetCached(string host, out CachedResolution? cached)
    {
        if (cache.TryGetValue(host, out cached) && cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        if (cache.TryRemove(host, out _))
        {
            cached = null;
        }

        return false;
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveSystemDnsAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveWithDoHAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = new List<IPAddress>();
        foreach (var endpoint in DoHEndpoints)
        {
            foreach (var recordType in new[] { "A", "AAAA" })
            {
                try
                {
                    var url = $"{endpoint}?name={Uri.EscapeDataString(host)}&type={recordType}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));

                    using var response = await DohHttp.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var payload = await JsonSerializer.DeserializeAsync<DohResponse>(stream, JsonOptions, cancellationToken);
                    if (payload?.Status != 0 || payload.Answer.Count == 0)
                    {
                        continue;
                    }

                    foreach (var answer in payload.Answer)
                    {
                        if (IPAddress.TryParse(answer.Data, out var address) && !addresses.Contains(address))
                        {
                            addresses.Add(address);
                        }
                    }
                }
                catch
                {
                    // Try the next provider / record type.
                }
            }

            if (addresses.Count > 0)
            {
                return addresses;
            }
        }

        return addresses;
    }

    private sealed record CachedResolution(IReadOnlyList<IPAddress> Addresses, DateTimeOffset ExpiresAt);

    private sealed class DohResponse
    {
        [JsonPropertyName("Status")]
        public int Status { get; set; }

        [JsonPropertyName("Answer")]
        public List<DohAnswer> Answer { get; set; } = new();
    }

    private sealed class DohAnswer
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;
    }
}
