using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class User
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string URL { get; set; } = "";

    [JsonPropertyName("username")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string NickName { get; set; } = "";

    [JsonPropertyName("avatar")]
    public AvatarMap Avatar { get; set; } = new();

    [JsonPropertyName("sign")]
    public string Signature { get; set; } = "";

    public class AvatarMap
    {
        [JsonPropertyName("large")]
        public string Large { get; set; } = "";

        [JsonPropertyName("medium")]
        public string Medium { get; set; } = "";

        [JsonPropertyName("small")]
        public string Small { get; set; } = "";
    }
}