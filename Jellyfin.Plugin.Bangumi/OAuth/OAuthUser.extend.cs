using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
#if EMBY
using System.IO;
using System.Net;
using MediaBrowser.Common.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif

namespace Jellyfin.Plugin.Bangumi.OAuth;

public partial class OAuthUser
{
    public string? Avatar { get; set; }

    public string? NickName { get; set; }

    public string? ProfileUrl { get; set; }

    public DateTime? EffectiveTime { get; set; }

    [JsonIgnore]
    public bool Expired => ExpireTime < DateTime.Now;

    public async Task GetProfile(BangumiApi api, CancellationToken cancellationToken = default)
    {
        var user = await api.GetAccountInfo(AccessToken, cancellationToken);
        if (user == null)
            return;
        Avatar = user.UserAvatar.Large;
        NickName = user.NickName;
        ProfileUrl = user.URL;
    }

    public async Task Refresh(
#if EMBY
        IHttpClient httpClient,
#else
        HttpClient httpClient,
#endif
        CancellationToken cancellationToken = default)
    {
        var formData = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", OAuthController.ApplicationId),
            new KeyValuePair<string, string>("client_secret", OAuthController.ApplicationSecret),
            new KeyValuePair<string, string>("refresh_token", RefreshToken)
        ]!);

#if EMBY
        var options = new HttpRequestOptions
        {
            Url = "https://bgm.tv/oauth/access_token",
            RequestHttpContent = formData,
            ThrowOnErrorResponse = false
        };
        var response = await httpClient.SendAsync(options, "POST");
        var isFailed = response.StatusCode >= HttpStatusCode.MovedPermanently;
        var stream = new StreamReader(response.Content);
        var responseBody = await stream.ReadToEndAsync(cancellationToken);
#else
        var response = await httpClient.PostAsync("https://bgm.tv/oauth/access_token", formData, cancellationToken);
        var isFailed = !response.IsSuccessStatusCode;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
#endif
        if (isFailed)
        {
            var error = JsonSerializer.Deserialize<OAuthError>(responseBody, Constants.JsonSerializerOptions)!;
            throw new HttpIOException(HttpRequestError.InvalidResponse, error.ErrorDescription);
        }

        var newUser = JsonSerializer.Deserialize<OAuthUser>(responseBody, Constants.JsonSerializerOptions)!;
        AccessToken = newUser.AccessToken;
        RefreshToken = newUser.RefreshToken;
        ExpireTime = newUser.ExpireTime;
    }
}
