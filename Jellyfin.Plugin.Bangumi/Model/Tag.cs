using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class Tag
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("count")]
    public int Count { get; set; }
}