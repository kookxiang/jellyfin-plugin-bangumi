using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi(IHttpClientFactory httpClientFactory, ArchiveData archive, OAuthStore store, Logger<BangumiApi> logger)
{
    private readonly Plugin _plugin = Plugin.Instance!;

    public Task<string> Get(string url, string? accessToken, CancellationToken token, bool useCache = true)
    {
        return Send(new HttpRequestMessage(HttpMethod.Get, url), accessToken, token, useCache);
    }

    public async Task<T?> Get<T>(string url, CancellationToken token, bool useCache = true)
    {
        return await Get<T>(url, store.GetAvailable()?.AccessToken, token, useCache);
    }

    public async Task<T?> Get<T>(string url, string? accessToken, CancellationToken token, bool useCache = true)
    {
        var jsonString = await Get(url, accessToken, token, useCache);
        return JsonSerializer.Deserialize<T>(jsonString, Constants.JsonSerializerOptions);
    }

    public async Task<string> Post(string url, HttpContent content, string? accessToken, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        return await Send(request, accessToken, token);
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
        var httpClient = GetHttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (store.GetAvailable() != null) request.Headers.Authorization = AuthenticationHeaderValue.Parse("Bearer " + store.GetAvailable()?.AccessToken);

        using var response = await httpClient.SendAsync(request, token);
        return response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
            ? response.Headers.Location?.ToString()
            : null;
    }

    public async Task<MemoryStream> FetchStream(string url, IProgress<double> progress, CancellationToken token)
    {
        using var httpClient = GetHttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var httpStream = await response.Content.ReadAsStreamAsync(token);
        var totalSize = response.Content.Headers.ContentLength;
        var memoryStream = new MemoryStream();
        int bytesRead;
        var totalRead = 0D;
        var buffer = new byte[65536].AsMemory();
        while ((bytesRead = await httpStream.ReadAsync(buffer, token)) != 0)
        {
            totalRead += bytesRead;
            if (totalSize != null) progress.Report(totalRead / totalSize.Value);

            await memoryStream.WriteAsync(buffer[..bytesRead], token);
        }

        memoryStream.Seek(0, SeekOrigin.Begin);
        progress.Report(100);
        return memoryStream;
    }

    private async Task<string> Send(HttpRequestMessage request, CancellationToken token)
    {
        return await Send(request, store.GetAvailable()?.AccessToken, token);
    }

    public HttpClient GetHttpClient()
    {
        var httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.Bangumi", _plugin.Version.ToString()));
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(https://github.com/kookxiang/jellyfin-plugin-bangumi)"));
        httpClient.Timeout = TimeSpan.FromMilliseconds(_plugin.Configuration.RequestTimeout);
        return httpClient;
    }

    public HttpClient GetHttpClient(HttpClientHandler handler)
    {
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.Bangumi", _plugin.Version.ToString()));
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(https://github.com/kookxiang/jellyfin-plugin-bangumi)"));
        httpClient.Timeout = TimeSpan.FromMilliseconds(_plugin.Configuration.RequestTimeout);
        return httpClient;
    }
}

public class JsonContent : StringContent
{
    public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Constants.JsonSerializerOptions), Encoding.UTF8, "application/json")
    {
        Headers.ContentType!.CharSet = null;
    }
}
