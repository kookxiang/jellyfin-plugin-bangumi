using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Connect;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using Microsoft.AspNetCore.Authorization;

using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
using MediaBrowser.Controller.Api;

namespace Jellyfin.Plugin.Bangumi.OAuth;


public class AuthState
{
    public object? id { get; set; }
    public object? effective { get; set; }
    public object? expire { get; set; }
    public object? avatar { get; set; }
    public object? nickname { get; set; }
    public object? url { get; set; }
}

[Route("/Bangumi/OAuthState", "GET")]
// [Authenticated]
public class OAuthState : IReturn<AuthState>
{
}

[Route("/Bangumi/RefreshOAuthToken", "POST")]
// [Authenticated]
public class RefreshOAuthToken : IReturnVoid
{
}

[Route("/Bangumi/OAuth", "DELETE")]
// [Authenticated]
public class DeAuth : IReturnVoid
{
}

[Route("/Bangumi/Redirect", "GET")]
public class Redirect : IReturn<string>
{
    public string prefix { get; set; } = string.Empty;
    public string user { get; set; } = string.Empty;
}

[Route("/Bangumi/OAuth", "GET")]
public class OAuth : IReturn<string>
{
    public string code { get; set; } = string.Empty;
    public string user { get; set; } = string.Empty;
}

[Unauthenticated]
public class OAuthController : BaseApiService
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

    private static string? _oAuthPath;
    private readonly BangumiApi _api;
    private readonly IUserManager _userManager;
    private readonly OAuthStore _store;
    private readonly ISessionContext _sessionContext;
    private readonly ISessionManager _sessionManager;

    ILogger _log;

    public OAuthController(IUserManager userManager, BangumiApi api, OAuthStore store, ILogger log, ISessionContext sessionContext, ISessionManager sessionManager)
    {
        _userManager = userManager;
        _api = api;
        _store = store;
        _log = log;
        _sessionContext = sessionContext;
        _sessionManager = sessionManager;
    }

    public async Task<object?> Get(OAuthState oAuthState)
    {
        var user = _sessionContext.GetUser(Request) ?? throw new ResourceNotFoundException();
        var info = _store.Get(user.Id) ?? throw new ResourceNotFoundException();

        if (string.IsNullOrEmpty(info.Avatar))
        {
            await info.GetProfile(_api);
            _store.Save();
        }

        return new AuthState
        {
            id = info.UserId,
            effective = info.EffectiveTime,
            expire = info.ExpireTime,
            avatar = info.Avatar,
            nickname = info.NickName,
            url = info.ProfileUrl
        };
    }

    public async Task Post(RefreshOAuthToken refreshOAuthToken)
    {
        var user = await Task.Run(() => _sessionContext.GetUser(Request)) ?? throw new ResourceNotFoundException();
        var info = _store.Get(user.Id) ?? throw new ResourceNotFoundException();
        await info.Refresh(_api.GetHttpClient());
        await info.GetProfile(_api);
        _store.Save();
    }

    public void Delete(DeAuth deAuth)
    {
        var user = _sessionContext.GetUser(Request) ?? throw new ResourceNotFoundException();
        _store.Delete(user.Id);
        _store.Save();
    }

    public string Get(Redirect redirect)
    {
        _oAuthPath = $"{redirect.prefix}/Bangumi/OAuth";
        var redirectUri = Uri.EscapeDataString($"{_oAuthPath}?user={redirect.user}");
        var url = $"https://bgm.tv/oauth/authorize?client_id={ApplicationId}&redirect_uri={redirectUri}&response_type=code";
        return url;
    }

    public async Task<string> Get(OAuth oAuth)
    {
        var urlPrefix = _oAuthPath ?? throw new Exception("Please reopen the authorization page");
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", ApplicationId),
            new KeyValuePair<string, string>("client_secret", ApplicationSecret),
            new KeyValuePair<string, string>("code", oAuth.code),
            new KeyValuePair<string, string>("redirect_uri", $"{urlPrefix}?user={oAuth.user}")
        });
        var options = new HttpRequestOptions
        {
            Url = "https://bgm.tv/oauth/access_token",
            RequestHttpContent = formData
        };
        var response = await _api.GetHttpClient().SendAsync(options, "POST");
        var isFailed = response.StatusCode >= HttpStatusCode.MovedPermanently;
        var stream = new StreamReader(response.Content);
        var responseBody = await stream.ReadToEndAsync();
        if (isFailed) return responseBody;
        var result = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
        result.EffectiveTime = DateTime.Now;
        await result.GetProfile(_api);
        _log.Info($"UserName: {result.NickName}, ProfileUrl: {result.ProfileUrl}");
        _store.Set(oAuth.user, result);
        _store.Save();

        return $"Authenticate success for {result.NickName}, please close the window and refresh the Emby window";
    }
}
