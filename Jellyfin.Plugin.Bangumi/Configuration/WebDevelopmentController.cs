#if DEBUG
using System;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Bangumi.Configuration;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/Bangumi/Web/Development")]
public class WebDevelopmentController : ControllerBase
{
    internal static string? GetServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("BANGUMI_WEB_DEV_SERVER must be an HTTP(S) server URL without credentials, query or fragment.");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    internal static string? ServerUrl => GetServerUrl(Environment.GetEnvironmentVariable("BANGUMI_WEB_DEV_SERVER"));

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get()
    {
        var url = ServerUrl;
        return url is null ? NotFound() : Ok(new { Url = url });
    }
}
#endif
