using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.OAuth;

[ApiController]
[Route("Plugins/Bangumi")]
public class OAuthController : ControllerBase
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

    private readonly BangumiApi _api;
    private readonly PluginDatabase _db;
    private readonly Plugin _plugin;
    private readonly ISessionContext _sessionContext;

    public OAuthController(BangumiApi api, PluginDatabase db, ISessionContext sessionContext, Plugin plugin)
    {
        _api = api;
        _db = db;
        _sessionContext = sessionContext;
        _plugin = plugin;
    }

    [HttpGet("OAuthState")]
    [Authorize("DefaultAuthorization")]
    public async Task<Dictionary<string, object?>?> OAuthState()
    {
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return null;
        var info = _db.Logins.FindById(user.Id);
        if (info == null)
            return null;

        if (string.IsNullOrEmpty(info.Avatar))
        {
            await info.GetProfile(_api);
            _db.Logins.Update(info);
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
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return BadRequest();
        var info = _db.Logins.FindById(user.Id);
        if (info == null)
            return BadRequest();
        await info.Refresh(_plugin.GetHttpClient(), user.Id);
        await info.GetProfile(_api);
        _db.Logins.Update(info);
        return Accepted();
    }

    [HttpDelete("OAuth")]
    [Authorize("DefaultAuthorization")]
    public async Task<ActionResult> DeAuth()
    {
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return BadRequest();
        _db.Logins.Delete(user.Id);
        return Accepted();
    }

    [HttpGet("OAuth")]
    public async Task<object?> OAuthCallback([FromQuery(Name = "code")] string code, [FromQuery(Name = "user")] string user)
    {
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ApplicationId),
            new KeyValuePair<string, string>("client_secret", ApplicationSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}?user={user}")
        });
        var response = await _plugin.GetHttpClient().PostAsync("https://bgm.tv/oauth/access_token", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return JsonSerializer.Deserialize<OAuthError>(responseBody);
        var result = JsonSerializer.Deserialize<OAuthResponse>(responseBody)!.ToUser(Guid.Parse(user));
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(_api);
        _db.Logins.Upsert(result);
        return Content("<script>window.opener.postMessage('BANGUMI-OAUTH-COMPLETE'); window.close()</script>", "text/html");
    }
}