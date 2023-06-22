using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Model;

public class ServerException : Exception
{
    private ServerException(string message) : base(message)
    {
    }

    public static async Task ThrowFrom(HttpResponseMessage response)
    {
        var content = $"HTTP Error {(int)response.StatusCode} {response.ReasonPhrase}";
        var exception = new Exception($"unknown response from bangumi server: {content}");
        try
        {
            content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Response>(content);
            if (result?.Title != null)
                exception = new ServerException($"{result.Title}: {result.Error?.Message ?? result.Description}");
        }
        catch (Exception)
        {
            // ignored
        }

        throw exception;
    }

    public class Response
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("details")]
        public ErrorDetail? Error { get; set; }
    }

    public class ErrorDetail
    {
        [JsonPropertyName("error")]
        public string Message { get; set; } = "";

        [JsonPropertyName("path")]
        public string RequestPath { get; set; } = "";

        [JsonPropertyName("method")]
        public string RequestMethod { get; set; } = "";
    }
}