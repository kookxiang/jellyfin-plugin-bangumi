using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Archive.Data;

public class Character
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("infobox")]
    public string RawInfoBox { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";
}
