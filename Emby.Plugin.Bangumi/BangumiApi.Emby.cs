using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClient _httpClient;

    public BangumiApi(IHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private static Plugin Plugin => Plugin.Instance!;

    public IHttpClient GetHttpClient()
    {
        return _httpClient;
    }

    private async Task<string> SendRequest(string method, HttpRequestOptions options)
    {
        options.UserAgent = $"Jellyfin.Plugin.Bangumi/{Plugin.Version} (https://github.com/kookxiang/jellyfin-plugin-bangumi)";
        using var response = await _httpClient.SendAsync(options, method);
        if (response.StatusCode >= HttpStatusCode.MovedPermanently) await ServerException.ThrowFrom(response);
        using var stream = new StreamReader(response.Content);
        return await stream.ReadToEndAsync();
    }

    private async Task<T?> SendRequest<T>(string url, CancellationToken token)
    {
        var jsonString = await SendRequest("GET", new HttpRequestOptions { Url = url });
        return JsonSerializer.Deserialize<T>(jsonString, Options);
    }

    private Task<T?> SendRequest<T>(string url, string? accessToken, CancellationToken token)
    {
        return SendRequest<T>(url, token);
    }
    
    public class JsonContent : StringContent
    {
        public JsonContent(object obj) : base(JsonSerializer.Serialize(obj, Options), Encoding.UTF8, "application/json")
        {
            Headers.ContentType!.CharSet = null;
        }
    }
}