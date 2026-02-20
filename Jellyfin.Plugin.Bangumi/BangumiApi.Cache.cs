using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        ExpirationScanFrequency = TimeSpan.FromMinutes(1),
        SizeLimit = 256 * 1024 * 1024
    });

    private async Task<string> Send(HttpRequestMessage request, string? accessToken, CancellationToken token, bool useCache = true)
    {
        if (!useCache || request.RequestUri == null)
            return await SendWithOutCache(request, accessToken, token);

        if (request.Method != HttpMethod.Get)
        {
            _cache.Remove(request.RequestUri.ToString());
            return await SendWithOutCache(request, accessToken, token);
        }

        var cacheKey = request.RequestUri.ToString();

        // Check if already in cache
        if (_cache.TryGetValue<string>(cacheKey, out var cachedValue) && cachedValue != null)
        {
            return cachedValue;
        }

        // Not in cache, fetch with cancellation support
        logger.Info("request api without cache: {url}", request.RequestUri);
        var response = await SendWithOutCache(request, accessToken, token);

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            Size = response.Length,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
            SlidingExpiration = TimeSpan.FromHours(6)
        };

        _cache.Set(cacheKey, response, cacheEntryOptions);
        return response;
    }

    private async Task<string> SendWithOutCache(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        using var httpClient = GetHttpClient();
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + accessToken);
        using var response = await httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) await HandleHttpException(response);
        return await response.Content.ReadAsStringAsync(token);
    }
}
