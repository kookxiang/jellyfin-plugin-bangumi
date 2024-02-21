﻿using System;
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
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return null;
        _store.Load();
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
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return BadRequest();
        _store.Load();
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
        var user = await _sessionContext.GetUser(Request);
        if (user == null)
            return BadRequest();
        _store.Load();
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
        var response = await _api.GetHttpClient().PostAsync("https://bgm.tv/oauth/access_token", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return JsonSerializer.Deserialize<OAuthError>(responseBody);
        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(_api);
        _store.Load();
        _store.Set(user, result);
        _store.Save();
        return Content("<script>window.opener.postMessage('BANGUMI-OAUTH-COMPLETE'); window.close()</script>", "text/html");
    }
}