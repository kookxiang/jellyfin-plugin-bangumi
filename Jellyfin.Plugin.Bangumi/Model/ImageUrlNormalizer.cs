using System;

namespace Jellyfin.Plugin.Bangumi.Model;

internal static class ImageUrlNormalizer
{
    private const string BangumiImageHost = "lain.bgm.tv";

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, BangumiImageHost, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        }.Uri.AbsoluteUri;
    }
}
