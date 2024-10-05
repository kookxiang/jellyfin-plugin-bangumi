using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        ExpirationScanFrequency = TimeSpan.FromMinutes(1),

        SizeLimit = 256 * 1024 * 1024
    });

    private Task<string> SendRequest(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        return SendRequestWithCache(request, accessToken, token);
    }

    private Task<string> SendRequestWithCache(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        if (request.RequestUri == null) return SendRequestWithOutCache(request, accessToken, token);
        if (request.Method != HttpMethod.Get)
        {
            _cache.Remove(request.RequestUri.ToString());
            return SendRequestWithOutCache(request, accessToken, token);
        }

        return _cache.GetOrCreateAsync<string>(request.RequestUri.ToString(), async entry =>
        {
            logger.LogInformation("request api without cache: {url}", request.RequestUri);
            var response = await SendRequestWithOutCache(request, accessToken, CancellationToken.None);
            entry.Size = response.Length;
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            entry.SlidingExpiration = TimeSpan.FromHours(6);
            return response;
        })!;
    }

    private async Task<string> SendRequestWithOutCache(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        var httpClient = GetHttpClient();
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + accessToken);
        using var response = await httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) await ServerException.ThrowFrom(response);
        return await response.Content.ReadAsStringAsync(token);
    }
}