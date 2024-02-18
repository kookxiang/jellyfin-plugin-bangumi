using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

#if EMBY
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
#endif

namespace Jellyfin.Plugin.Bangumi.OAuth;

[ApiController]
[Route("/Bangumi")]
public class OAuthController : ControllerBase
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

    private static string? _oAuthPath;

    private readonly BangumiApi _api;
    private readonly ISessionContext _sessionContext;
    private readonly OAuthStore _store;

    public OAuthController(BangumiApi api, OAuthStore store, ISessionContext sessionContext)
    {
        _api = api;
        _store = store;
        _sessionContext = sessionContext;
    }

    [HttpGet("OAuthState")]
    [Authorize("DefaultAuthorization")]
    public async Task<Dictionary<string, object?>?> OAuthState()
    {
#if EMBY
        var user = await Task.Run(() => _sessionContext.GetUser(Request));
#else
        var user = await _sessionContext.GetUser(Request);
#endif
        if (user == null)
            return null;
        var info = _store.Get(user.Id);
        if (info == null)
            return null;

        if (string.IsNullOrEmpty(info.Avatar))
        {
            await info.GetProfile(_api);
            _store.Save();
        }

        return new Dictionary<string, object?>
        {
            ["id"] = info.UserId,
            ["effective"] = info.EffectiveTime,
            ["expire"] = info.ExpireTime,
            ["avatar"] = info.Avatar,
            ["nickname"] = info.NickName,
            ["url"] = info.ProfileUrl
        };
    }

    [HttpPost("RefreshOAuthToken")]
    [Authorize("DefaultAuthorization")]
    public async Task<ActionResult> RefreshOAuthToken()
    {
#if EMBY
        var user = await Task.Run(() => _sessionContext.GetUser(Request));
#else
        var user = await _sessionContext.GetUser(Request);
#endif
        if (user == null)
            return BadRequest();
        var info = _store.Get(user.Id);
        if (info == null)
            return BadRequest();
        await info.Refresh(_api.GetHttpClient());
        await info.GetProfile(_api);
        _store.Save();
        return Accepted();
    }

    [HttpDelete("OAuth")]
    [Authorize("DefaultAuthorization")]
    public async Task<ActionResult> DeAuth()
    {
#if EMBY
        var user = await Task.Run(() => _sessionContext.GetUser(Request));
#else
        var user = await _sessionContext.GetUser(Request);
#endif
        if (user == null)
            return BadRequest();
        _store.Delete(user.Id);
        _store.Save();
        return Accepted();
    }

    [HttpGet("Redirect")]
    public Task<ActionResult> SetCallbackUrl([FromQuery(Name = "prefix")] string urlPrefix, [FromQuery(Name = "user")] string user)
    {
        _oAuthPath = $"{urlPrefix}/Bangumi/OAuth";
        var redirectUri = Uri.EscapeDataString($"{_oAuthPath}?user={user}");
        return Task.FromResult<ActionResult>(
            Redirect($"https://bgm.tv/oauth/authorize?client_id={ApplicationId}&redirect_uri={redirectUri}&response_type=code"));
    }

    [HttpGet("OAuth")]
    public async Task<object?> OAuthCallback([FromQuery(Name = "code")] string code, [FromQuery(Name = "user")] string user)
    {
        var urlPrefix = _oAuthPath ?? $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}";
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ApplicationId),
            new KeyValuePair<string, string>("client_secret", ApplicationSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", $"{urlPrefix}?user={user}")
        });
#if EMBY
        var options = new HttpRequestOptions
        {
            Url = "https://bgm.tv/oauth/access_token",
            RequestHttpContent = formData
        };
        var response = await _api.GetHttpClient().SendAsync(options, "POST");
        var isFailed = response.StatusCode >= HttpStatusCode.MovedPermanently;
        var stream = new StreamReader(response.Content);
        var responseBody = await stream.ReadToEndAsync();
#else
        var response = await _api.GetHttpClient().PostAsync("https://bgm.tv/oauth/access_token", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        var isFailed = !response.IsSuccessStatusCode;
#endif
        if (isFailed) return JsonSerializer.Deserialize<OAuthError>(responseBody);
        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(_api);
        _store.Set(user, result);
        _store.Save();
        return Content("<script>window.opener.postMessage('BANGUMI-OAUTH-COMPLETE'); window.close()</script>", "text/html");
    }
}