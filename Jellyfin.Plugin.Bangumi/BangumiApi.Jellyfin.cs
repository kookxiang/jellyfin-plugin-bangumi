using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi(IHttpClientFactory httpClientFactory, OAuthStore store, ILogger<BangumiApi> logger)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Plugin _plugin = Plugin.Instance!;

    private Task<string> Get(string url, string? accessToken, CancellationToken token)
    {
        return Send(new HttpRequestMessage(HttpMethod.Get, url), accessToken, token);
    }

    private async Task<T?> Get<T>(string url, CancellationToken token)
    {
        return await Get<T>(url, store.GetAvailable()?.AccessToken, token);
    }

    private async Task<T?> Get<T>(string url, string? accessToken, CancellationToken token)
    {
        var jsonString = await Get(url, accessToken, token);
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    private async Task<string> Post(string url, HttpContent content, string? accessToken, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        return await Send(request, accessToken, token);
    }

    private Task<T?> Post<T>(string url, HttpContent content, CancellationToken token)
    {
        return Post<T>(url, content, null, token);
    }

    private async Task<T?> Post<T>(string url, HttpContent content, string? accessToken, CancellationToken token)
    {
        var jsonString = await Post(url, content, accessToken, token);
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    private async Task<string?> FollowRedirection(string url, CancellationToken token)
    {
        var httpClient = GetHttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, token);
        return response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect ? response.Headers.Location?.ToString() : null;
    }

    private async Task<string> Send(HttpRequestMessage request, CancellationToken token)
    {
        return await Send(request, store.GetAvailable()?.AccessToken, token);
    }

    public HttpClient GetHttpClient()
    {
        var httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.Bangumi", _plugin.Version.ToString()));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/kookxiang/jellyfin-plugin-bangumi)"));
        httpClient.Timeout = TimeSpan.FromMilliseconds(_plugin.Configuration.RequestTimeout);
        return httpClient;
    }

    public HttpClient GetHttpClient(HttpClientHandler handler)
    {
        var httpClient = new HttpClient(handler);
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