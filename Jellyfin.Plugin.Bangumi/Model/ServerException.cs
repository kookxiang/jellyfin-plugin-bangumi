using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Model;

public class ServerException : Exception
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private ServerException(string message) : base(message)
    {
    }

    public static async Task ThrowFrom(HttpResponseMessage response)
    {
        var content = "<empty>";
        var exception = new Exception($"unknown response from bangumi server: {content}");
        try
        {
            content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Response>(content, Options);
            if (result?.Title != null)
                exception = new ServerException($"{result.Title}: {result.Details?.Error ?? result.Description}");
        }
        catch (Exception)
        {
            // ignored
        }

        throw exception;
    }

    private class Response
    {
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public ErrorDetail? Details { get; set; }
    }

    private class ErrorDetail
    {
        public string Error { get; } = "";

        public string Path { get; set; } = "";

        public string Method { get; set; } = "";
    }
}