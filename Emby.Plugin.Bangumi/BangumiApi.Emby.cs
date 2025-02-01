using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi(IHttpClient httpClient, OAuthStore store)
{
    private static Plugin Plugin => Plugin.Instance!;

    public IHttpClient GetHttpClient()
    {
        return httpClient;
    }

    public async Task<string> Send(string method, HttpRequestOptions options, CancellationToken token)
    {
        options.UserAgent = $"Jellyfin.Plugin.Bangumi/{Plugin.Version} (https://github.com/kookxiang/jellyfin-plugin-bangumi)";
        options.TimeoutMs = Plugin.Configuration.RequestTimeout;
        options.ThrowOnErrorResponse = false;
        using var response = await httpClient.SendAsync(options, method);
        if (response.StatusCode >= HttpStatusCode.MovedPermanently) await HandleHttpException(response);
        using var stream = new StreamReader(response.Content);
        return await stream.ReadToEndAsync(token);
    }

    public Task<T?> Get<T>(string url, CancellationToken token)
    {
        return Get<T>(url, store.GetAvailable()?.AccessToken, token);
    }

    public async Task<T?> Get<T>(string url, string? accessToken, CancellationToken token, bool useCache = true)
    {
        var options = new HttpRequestOptions { Url = url };
        if (accessToken != null)
            options.RequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var jsonString = await Send("GET", options, token);
        return JsonSerializer.Deserialize<T>(jsonString, Constants.JsonSerializerOptions);
    }

    public async Task<string> Post(string url, HttpContent content, string? accessToken, CancellationToken token)
    {
        using var response = await httpClient.SendAsync(new HttpRequestOptions
            {
                Url = url,
                RequestHttpContent = content,
                RequestHeaders = { { "Authorization", "Bearer " + accessToken } },
                UserAgent = $"Jellyfin.Plugin.Bangumi/{Plugin.Version} (https://github.com/kookxiang/jellyfin-plugin-bangumi)",
                TimeoutMs = Plugin.Configuration.RequestTimeout,
                ThrowOnErrorResponse = false
            },
            "POST");
        if (response.StatusCode >= HttpStatusCode.MovedPermanently) await HandleHttpException(response);
        using var stream = new StreamReader(response.Content);
        return await stream.ReadToEndAsync(token);
    }

    public Task<T?> Post<T>(string url, HttpContent content)
    {
        return Post<T>(url, content, null);
    }

    public Task<T?> Post<T>(string url, HttpContent content, string? accessToken)
    {
        return Post<T>(url, content, accessToken, CancellationToken.None);
    }

    public Task<T?> Post<T>(string url, HttpContent content, CancellationToken token)
    {
        return Post<T>(url, content, null, token);
    }

    public async Task<T?> Post<T>(string url, HttpContent content, string? accessToken, CancellationToken token)
    {
        var jsonString = await Post(url, content, accessToken, token);
        return JsonSerializer.Deserialize<T>(jsonString, Constants.JsonSerializerOptions);
    }

    public async Task<string?> FollowRedirection(string url, CancellationToken token)
    {
        var options = new HttpRequestOptions { Url = url };
        using var response = await httpClient.SendAsync(options, "GET");
        return response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
            ? response.Headers.GetValueOrDefault("Location")
            : null;
    }
}

public class JsonContent : StringContent
{
    public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Constants.JsonSerializerOptions), Encoding.UTF8, "application/json")
    {
        Headers.ContentType!.CharSet = null;
    }
}
