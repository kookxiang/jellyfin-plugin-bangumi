using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Net;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private static async Task HandleHttpException(HttpResponseInfo response)
    {
        var content = "<empty>";
        var exception = new HttpException($"unknown response from bangumi server: {content}");
        try
        {
            using var stream = new StreamReader(response.Content);
            content = await stream.ReadToEndAsync();
            var result = JsonSerializer.Deserialize<Response>(content, Constants.JsonSerializerOptions);
            if (result?.Title != null)
                exception = new HttpException($"{result.Title}: {result.Description}");
        }
        catch (Exception)
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
