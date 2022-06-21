using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public partial class OAuthUser
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = null!;

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("expires_time")]
    public DateTime ExpireTime { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpireIn
    {
        set => ExpireTime = DateTime.Now.AddSeconds(value);
    }
}