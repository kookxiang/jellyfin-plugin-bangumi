using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.OAuth;

[ApiController]
[Route("Plugins/Bangumi")]
public class OAuthController(
    BangumiApi api,
    OAuthStore store,
    IAuthorizationContext authorizationContext,
    IUserManager userManager)
    : ControllerBase
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

    [HttpGet("OAuthUsers")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<Dictionary<string, string>>> OAuthUsers()
    {
        return Ok(userManager.Users
            .OrderBy(user => user.Username)
            .Select(user => new Dictionary<string, string>
            {
                ["id"] = user.Id.ToString("N"),
                ["name"] = user.Username
            }));
    }

    [HttpGet("OAuthState")]
    [Authorize]
    public async Task<Dictionary<string, object?>?> OAuthState([FromQuery] string? userId = null)
    {
        var targetUserId = await GetTargetUserId(userId);
        if (targetUserId == null)
            return null;
        store.Load();
        var info = store.GetStored(targetUserId.Value);
        if (info == null)
            return null;

        if (!info.Expired && string.IsNullOrEmpty(info.Avatar))
        {
            await info.GetProfile(api);
            store.Save();
        }

        return new Dictionary<string, object?>
        {
            ["id"] = info.UserId,
            ["effective"] = info.EffectiveTime,
            ["expire"] = info.ExpireTime,
            ["avatar"] = info.Avatar,
            ["nickname"] = info.NickName ?? info.UserName,
            ["url"] = info.ProfileUrl,
            ["autoRefresh"] = !string.IsNullOrEmpty(info.RefreshToken),
            ["expired"] = info.Expired
        };
    }

    [HttpPost("RefreshOAuthToken")]
    [Authorize]
    public async Task<ActionResult> RefreshOAuthToken([FromQuery] string? userId = null)
    {
        var targetUserId = await GetTargetUserId(userId);
        if (targetUserId == null)
            return Forbid();
        store.Load();
        var info = store.GetStored(targetUserId.Value);
        if (info == null)
            return BadRequest();
        using var httpClient = api.GetHttpClient();
        await info.Refresh(httpClient);
        await info.GetProfile(api);
        store.Save();
        return Accepted();
    }

    [HttpDelete("OAuth")]
    [Authorize]
    public async Task<ActionResult> DeAuth([FromQuery] string? userId = null)
    {
        var targetUserId = await GetTargetUserId(userId);
        if (targetUserId == null)
            return Forbid();
        store.Load();
        store.Delete(targetUserId.Value);
        store.Save();
        return Accepted();
    }

    [HttpGet("Redirect")]
    public Task<ActionResult> SetCallbackUrl([FromQuery(Name = "prefix")] string urlPrefix, [FromQuery(Name = "user")] string user)
    {
        if (!TryNormalizeServerUrl(urlPrefix, out var normalizedPrefix)
            || !Guid.TryParse(user, out var userId)
            || userManager.GetUserById(userId) == null)
            return Task.FromResult<ActionResult>(BadRequest());

        var callbackUrl = GetOAuthCallbackUrl(normalizedPrefix, userId.ToString("N"));
        var redirectUri = Uri.EscapeDataString(callbackUrl);
        return Task.FromResult<ActionResult>(
            Redirect($"{BangumiApi.BaseWebsiteUrl}/oauth/authorize?client_id={ApplicationId}&redirect_uri={redirectUri}&response_type=code"));
    }

    [HttpGet("OAuth")]
    public async Task<object?> OAuthCallback(
        [FromQuery(Name = "code")] string code,
        [FromQuery(Name = "user")] string user,
        [FromQuery(Name = "prefix")] string? urlPrefix = null)
    {
        if (!Guid.TryParse(user, out var userId) || userManager.GetUserById(userId) == null)
            return BadRequest();

        var normalizedUserId = userId.ToString("N");
        var callbackUrl = TryNormalizeServerUrl(urlPrefix, out var normalizedPrefix)
            ? GetOAuthCallbackUrl(normalizedPrefix, normalizedUserId)
            : $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}?user={normalizedUserId}";
        using var formData = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ApplicationId),
            new KeyValuePair<string, string>("client_secret", ApplicationSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", callbackUrl)
        ]);
        using var httpClient = api.GetHttpClient();
        var response = await httpClient.PostAsync($"{BangumiApi.BaseWebsiteUrl}/oauth/access_token", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return JsonSerializer.Deserialize<OAuthError>(responseBody, Constants.JsonSerializerOptions);
        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody, Constants.JsonSerializerOptions)!;
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(api);
        store.Load();
        store.Set(normalizedUserId, result);
        store.Save();
        return Content("""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Bangumi 授权成功</title></head>
            <body style="font-family: sans-serif; text-align: center; padding: 3rem 1rem">
            <h1>授权成功</h1><p>Bangumi 账号已经绑定，可以关闭此页面。</p>
            <script>if (window.opener) { window.opener.postMessage('BANGUMI-OAUTH-COMPLETE', '*'); window.close(); }</script>
            </body></html>
            """, "text/html");
    }

    [HttpPatch("AccessToken")]
    [Authorize]
    public async Task<ActionResult> SetAccessTokenManually(
        [FromForm(Name = "token")] string accessToken,
        [FromQuery] string? userId = null)
    {
        var targetUserId = await GetTargetUserId(userId);
        if (targetUserId == null)
            return Forbid();
        using var formData = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("access_token", accessToken)
        ]);;
        using var httpClient = api.GetHttpClient();
        var response = await httpClient.PostAsync($"{BangumiApi.BaseWebsiteUrl}/oauth/token_status", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<OAuthError>(responseBody, Constants.JsonSerializerOptions);
            return Problem(error?.ErrorDescription);
        }

        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody, Constants.JsonSerializerOptions)!;
        result.AccessToken = accessToken;
        result.EffectiveTime = DateTime.Now;
        result.RefreshToken = "";
        store.Load();
        store.Set(targetUserId.Value, result);
        store.Save();
        return Accepted();
    }

    private async Task<Guid?> GetTargetUserId(string? requestedUserId)
    {
        var authorizationInfo = await authorizationContext.GetAuthorizationInfo(Request);
        var currentUser = authorizationInfo.User;
        if (currentUser == null)
            return null;

        if (string.IsNullOrEmpty(requestedUserId))
            return currentUser.Id;

        if (!Guid.TryParse(requestedUserId, out var targetUserId)
            || userManager.GetUserById(targetUserId) == null)
            return null;

        if (targetUserId == currentUser.Id)
            return targetUserId;

        var isAdministrator = currentUser.Permissions.Any(permission =>
            permission.Kind == PermissionKind.IsAdministrator && permission.Value);
        return isAdministrator ? targetUserId : null;
    }

    private static bool TryNormalizeServerUrl(string? url, out string normalizedUrl)
    {
        normalizedUrl = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        normalizedUrl = url!.TrimEnd('/');
        return true;
    }

    private static string GetOAuthCallbackUrl(string urlPrefix, string userId)
    {
        return $"{urlPrefix}/Plugins/Bangumi/OAuth?user={Uri.EscapeDataString(userId)}&prefix={Uri.EscapeDataString(urlPrefix)}";
    }
}
