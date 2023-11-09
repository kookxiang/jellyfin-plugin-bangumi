using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly Plugin _plugin;
    private readonly OAuthStore _store;

    public BangumiApi(IHttpClientFactory httpClientFactory, OAuthStore store)
    {
        _plugin = Plugin.Instance!;
        _httpClientFactory = httpClientFactory;
        _store = store;
    }

    private Task<string> SendRequest(string url, string? accessToken, CancellationToken token)
    {
        return SendRequest(new HttpRequestMessage(HttpMethod.Get, url), accessToken, token);
    }

    private async Task<string> SendRequest(HttpRequestMessage request, CancellationToken token)
    {
        return await SendRequest(request, _store.GetAvailable()?.AccessToken, token);
    }

    private async Task<string> SendRequest(HttpRequestMessage request, string? accessToken, CancellationToken token)
    {
        var httpClient = GetHttpClient();
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + accessToken);
        using var response = await httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) await ServerException.ThrowFrom(response);
        return await response.Content.ReadAsStringAsync(token);
    }

    private async Task<T?> SendRequest<T>(string url, CancellationToken token)
    {
        return await SendRequest<T>(url, _store.GetAvailable()?.AccessToken, token);
    }

    private async Task<T?> SendRequest<T>(string url, string? accessToken, CancellationToken token)
    {
        var jsonString = await SendRequest(url, accessToken, token);
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    public HttpClient GetHttpClient()
    {
        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.Bangumi", _plugin.Version.ToString()));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/kookxiang/jellyfin-plugin-bangumi)"));
        httpClient.Timeout = TimeSpan.FromMilliseconds(_plugin.Configuration.RequestTimeout);
        return httpClient;
    }

    public class JsonContent : StringContent
    {
        public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Options), Encoding.UTF8, "application/json")
        {
            Headers.ContentType!.CharSet = null;
        }
    }
}