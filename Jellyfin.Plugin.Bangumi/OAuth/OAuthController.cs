using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
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
    OAuthAuthorizationStore authorizationStore,
    IAuthorizationContext authorizationContext,
    IUserManager userManager)
    : ControllerBase
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

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
            ["nickname"] = string.IsNullOrWhiteSpace(info.NickName) ? info.UserName : info.NickName,
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

    [HttpPost("OAuth/Authorization")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, string>>> CreateAuthorization(
        [FromForm(Name = "prefix")] string urlPrefix,
        [FromQuery] string? userId = null)
    {
        var targetUserId = await GetTargetUserId(userId);
        if (!TryNormalizeServerUrl(urlPrefix, out var normalizedPrefix)
            || targetUserId == null)
            return BadRequest();

        var callbackUrl = GetOAuthCallbackUrl(normalizedPrefix);
        var authorization = authorizationStore.Create(targetUserId.Value, callbackUrl, normalizedPrefix);
        var redirectUri = Uri.EscapeDataString(callbackUrl);
        var state = Uri.EscapeDataString(authorization.State);
        return new Dictionary<string, string>
        {
            ["url"] = $"{BangumiApi.BaseWebsiteUrl}/oauth/authorize?client_id={ApplicationId}&redirect_uri={redirectUri}&response_type=code&state={state}"
        };
    }

    [HttpGet("OAuth")]
    public async Task<object?> OAuthCallback(
        [FromQuery(Name = "code")] string code,
        [FromQuery(Name = "state")] string state)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return BadRequest();
        var authorization = authorizationStore.Consume(state);
        if (authorization == null || userManager.GetUserById(authorization.UserId) == null)
            return BadRequest();

        using var formData = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ApplicationId),
            new KeyValuePair<string, string>("client_secret", ApplicationSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", authorization.CallbackUrl)
        ]);
        using var httpClient = api.GetHttpClient();
        var response = await httpClient.PostAsync($"{BangumiApi.BaseWebsiteUrl}/oauth/access_token", formData);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return JsonSerializer.Deserialize<OAuthError>(responseBody, Constants.JsonSerializerOptions);
        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody, Constants.JsonSerializerOptions)!;
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(api);
        store.Load();
        store.Set(authorization.UserId, result);
        store.Save();
        var targetOrigin = JsonSerializer.Serialize(new Uri(authorization.ServerUrl).GetLeftPart(UriPartial.Authority));
        return Content("""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Bangumi 授权成功</title></head>
            <body style="font-family: sans-serif; text-align: center; padding: 3rem 1rem">
            <h1>授权成功</h1><p>Bangumi 账号已经绑定，可以关闭此页面。</p>
            <script>if (window.opener) { window.opener.postMessage('BANGUMI-OAUTH-COMPLETE', TARGET_ORIGIN); window.close(); }</script>
            </body></html>
            """.Replace("TARGET_ORIGIN", targetOrigin, StringComparison.Ordinal), "text/html");
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

    private static string GetOAuthCallbackUrl(string urlPrefix)
    {
        return $"{urlPrefix}/Plugins/Bangumi/OAuth";
    }
}
