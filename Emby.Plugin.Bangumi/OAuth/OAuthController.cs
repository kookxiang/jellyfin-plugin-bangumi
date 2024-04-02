﻿using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using System.Text;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using System.Threading.Tasks;

using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

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
public class OAuthState : IReturn<AuthState>
{
}

[Route("/Bangumi/RefreshOAuthToken", "POST")]
public class RefreshOAuthToken : IReturnVoid
{
}

[Route("/Bangumi/OAuth", "DELETE")]
public class DeAuth : IReturnVoid
{
}

[Route("/Bangumi/Redirect", "GET")]
public class Redirect : IReturnVoid
{
    public string prefix { get; set; } = string.Empty;
    public string user { get; set; } = string.Empty;
}

[Route("/Bangumi/OAuth", "GET")]
public class OAuth : IReturnVoid
{
    public string code { get; set; } = string.Empty;
    public string user { get; set; } = string.Empty;
}

[Unauthenticated]
public class OAuthController : IService, IRequiresRequest
{
    protected internal const string ApplicationId = "bgm16185f43c213d11c9";
    protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

    private static string? _oAuthPath;
    private readonly BangumiApi _api;
    private readonly IUserManager _userManager;
    private readonly OAuthStore _store;
    private readonly ISessionContext _sessionContext;
    private readonly ISessionManager _sessionManager;

    public IRequest Request { get; set; }

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
        _store.Load();
        var info = _store.Get(user.Id);
        if (info == null)
        {
            return "null";
        }

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
        _store.Load();
        var info = _store.Get(user.Id) ?? throw new ResourceNotFoundException();
        await info.Refresh(_api.GetHttpClient());
        await info.GetProfile(_api);
        _store.Save();
    }

    public void Delete(DeAuth deAuth)
    {
        var user = _sessionContext.GetUser(Request) ?? throw new ResourceNotFoundException();
        _store.Load();
        _store.Delete(user.Id);
        _store.Save();
    }

    public void Get(Redirect redirect)
    {
        _oAuthPath = $"{redirect.prefix}/Bangumi/OAuth";
        var redirectUri = Uri.EscapeDataString($"{_oAuthPath}?user={redirect.user}");
        var url = $"https://bgm.tv/oauth/authorize?client_id={ApplicationId}&redirect_uri={redirectUri}&response_type=code";
        Request.Response.Redirect(url);
    }

    public async Task MakeHtmlResp(string content)
    {
        var response = Request.Response;
        response.ContentType = "text/html";
        response.StatusCode = (int)HttpStatusCode.OK;

        var writer = response.OutputWriter;
        var bytes = Encoding.UTF8.GetBytes(content);
        await writer.WriteAsync(bytes);
        await response.CompleteAsync();
    }

    public async Task Get(OAuth oAuth)
    {
        var urlPrefix = _oAuthPath;
        if (urlPrefix == null)
        {
            await MakeHtmlResp("Please reopen the authorization page");
            return;
        }
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
            RequestHttpContent = formData,
            ThrowOnErrorResponse = false
        };
        var response = await _api.GetHttpClient().SendAsync(options, "POST");
        var isFailed = response.StatusCode >= HttpStatusCode.MovedPermanently;
        try
        {
            var stream = new StreamReader(response.Content);
            var responseBody = await stream.ReadToEndAsync();
            if (isFailed)
            {
                await MakeHtmlResp(responseBody);
                return;
            }
            var result = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
            result.EffectiveTime = DateTime.Now;
            await result.GetProfile(_api);
            _log.Info($"UserName: {result.NickName}, ProfileUrl: {result.ProfileUrl}");
            _store.Load();
            _store.Set(oAuth.user, result);
            _store.Save();
        }
        catch (Exception e)
        {
            _log.Error(e.Message);
            await MakeHtmlResp($"Failed to handle bangumi callback: {e.Message}");
            return;
        }

        await MakeHtmlResp("<script>window.opener.postMessage('BANGUMI-OAUTH-COMPLETE'); window.close()</script>");
    }
}
