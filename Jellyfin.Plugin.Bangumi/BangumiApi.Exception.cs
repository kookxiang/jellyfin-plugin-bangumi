using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi;

public partial class BangumiApi
{
    private class ServerException : Exception
    {
        public readonly HttpStatusCode StatusCode;

        private ServerException(HttpStatusCode status, string message) : base(message)
        {
            StatusCode = status;
        }

        public static async Task ThrowFrom(HttpResponseMessage response)
        {
            var content = "<empty>";
            var exception = new Exception($"unknown response from bangumi server: {content}");
            try
            {
                content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Response>(content, Constants.JsonSerializerOptions);
                if (result?.Title != null)
                    exception = new ServerException(response.StatusCode, $"{result.Title}: {result.Description}");
            }
            catch (Exception)
            {
                // ignored
            }

            throw exception;
        }
    }

    private class Response
    {
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";
    }
}