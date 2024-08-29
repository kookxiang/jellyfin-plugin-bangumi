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
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi(IHttpClient httpClient, OAuthStore store)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static Plugin Plugin => Plugin.Instance!;

    public IHttpClient GetHttpClient()
    {
        return httpClient;
    }

    private async Task<string> SendRequest(string method, HttpRequestOptions options)
    {
        options.UserAgent = $"Jellyfin.Plugin.Bangumi/{Plugin.Version} (https://github.com/kookxiang/jellyfin-plugin-bangumi)";
        options.TimeoutMs = Plugin.Configuration.RequestTimeout;
        options.ThrowOnErrorResponse = false;
        using var response = await httpClient.SendAsync(options, method);
        if (response.StatusCode >= HttpStatusCode.MovedPermanently) await ServerException.ThrowFrom(response);
        using var stream = new StreamReader(response.Content);
        return await stream.ReadToEndAsync();
    }

    private Task<T?> SendRequest<T>(string url, CancellationToken token)
    {
        return SendRequest<T>(url, store.GetAvailable()?.AccessToken, token);
    }

    private async Task<T?> SendRequest<T>(string url, string? accessToken, CancellationToken token)
    {
        var options = new HttpRequestOptions { Url = url };
        if (accessToken != null)
            options.RequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        var jsonString = await SendRequest("GET", options);
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    public class JsonContent : StringContent
    {
        public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Options), Encoding.UTF8, "application/json")
        {
            Headers.ContentType!.CharSet = null;
        }
    }
}