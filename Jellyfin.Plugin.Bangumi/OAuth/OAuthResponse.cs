using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public class OAuthResponse
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

    public OAuthUser ToUser(Guid userId)
    {
        return new OAuthUser
        {
            Id = userId,
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            UserId = UserId,
            ExpireTime = ExpireTime
        };
    }
}