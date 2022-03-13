using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.OAuth
{
    [ApiController]
    [Route("Plugins/Bangumi")]
    public class OAuthController : ControllerBase
    {
        protected internal const string ApplicationId = "bgm16185f43c213d11c9";
        protected internal const string ApplicationSecret = "1b28040afd28882aecf23dcdd86be9f7";

        private readonly BangumiApi _api;
        private readonly OAuthStore _oAuthStore;
        private readonly Plugin _plugin;
        private readonly ISessionContext _sessionContext;

        public OAuthController(BangumiApi api, OAuthStore oAuthStore, ISessionContext sessionContext, Plugin plugin)
        {
            _api = api;
            _oAuthStore = oAuthStore;
            _sessionContext = sessionContext;
            _plugin = plugin;
        }

        [HttpGet("OAuthState")]
        [Authorize("DefaultAuthorization")]
        public async Task<Dictionary<string, object?>?> OAuthState()
        {
            var user = _sessionContext.GetUser(Request);
            var info = _oAuthStore.Get(user.Id);
            if (info == null)
                return null;
            return new Dictionary<string, object?>
            {
                ["id"] = info.UserId,
                ["effective"] = info.EffectiveTime,
                ["expire"] = info.ExpireTime,
                ["user"] = await _api.GetAccountInfo(info.AccessToken, CancellationToken.None)
            };
        }

        [HttpDelete("OAuth")]
        [Authorize("DefaultAuthorization")]
        public AcceptedResult DeAuth()
        {
            var user = _sessionContext.GetUser(Request);
            _oAuthStore.Delete(user.Id);
            _oAuthStore.Save();
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
                new KeyValuePair<string, string>("redirect_uri", $"{Request.Scheme}://{Request.Host}{Request.Path}?user={user}")
            }!);
            var response = await _plugin.GetHttpClient().PostAsync("https://bgm.tv/oauth/access_token", formData);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return JsonSerializer.Deserialize<OAuthError>(responseBody);
            var result = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
            result.EffectiveTime = DateTime.Now;
            _oAuthStore.Set(user, result);
            _oAuthStore.Save();
            return Content("<script>window.opener.postMessage('BANGUMI-OAUTH-COMPLETE'); window.close()</script>", "text/html");
        }
    }
}