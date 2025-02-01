using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class UserAvatarMap
{
    [JsonPropertyName("large")]
    public string Large { get; set; } = "";

    [JsonPropertyName("medium")]
    public string Medium { get; set; } = "";

    [JsonPropertyName("small")]
    public string Small { get; set; } = "";
}
