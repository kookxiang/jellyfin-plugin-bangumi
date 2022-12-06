using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Model;

public class ServerException : Exception
{
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public static async Task ThrowFrom(HttpResponseMessage response)
    {
        var content = "<empty>";
        var inner = new Exception($"unknown response from bangumi server: {content}");
        try
        {
            content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ServerException>(content);
            if (result?.Title != null)
                inner = new Exception($"{result.Title}: {result.Description}");
        }
        catch (Exception)
        {
            // ignored
        }

        throw new Exception($"request bangumi api failed, status: {response.StatusCode}", inner);
    }
}