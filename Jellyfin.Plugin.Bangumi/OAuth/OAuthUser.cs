using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public partial class OAuthUser
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("expires_time")]
    public DateTime ExpireTime { get; set; }

    [JsonPropertyName("effective_time")]
    public DateTime? EffectiveTime { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpireIn
    {
        get => (int)ExpireTime.Subtract(DateTime.Now).TotalSeconds;
        set => ExpireTime = DateTime.Now.AddSeconds(value);
    }
}
