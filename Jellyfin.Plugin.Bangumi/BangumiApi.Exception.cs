using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static async Task HandleHttpException(HttpResponseMessage response, CancellationToken token = default)
    {
        var requestUri = response.RequestMessage?.RequestUri;
        var statusCode = (int)response.StatusCode;
        Exception exception = new HttpIOException(HttpRequestError.Unknown, $"unknown response from {requestUri}: {response.ReasonPhrase}");
        try
        {
            var content = await response.Content.ReadAsStringAsync(token);
            exception = new HttpIOException(HttpRequestError.Unknown, $"unknown response from {requestUri} {statusCode}: {content}");
            var result = JsonSerializer.Deserialize<Response>(content, Constants.JsonSerializerOptions);
            if (result?.Title != null)
                exception = new HttpIOException(HttpRequestError.InvalidResponse, $"{result.Title}: {result.Description}");
        }
        catch
        {
            // ignored
        }

        throw exception;
    }

    private sealed class Response
    {
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";
    }
}
