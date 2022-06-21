using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public class OAuthError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = "";
}