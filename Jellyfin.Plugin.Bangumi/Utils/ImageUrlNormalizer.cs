using System;

namespace Jellyfin.Plugin.Bangumi.Utils;

internal static class ImageUrlNormalizer
{
    private const string BangumiImageHost = "lain.bgm.tv";
    private const string NoIconSubjectImageUrl = "https://lain.bgm.tv/img/no_icon_subject.png";

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, BangumiImageHost, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        try
        {
            return new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            }.Uri.AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return url;
        }
    }

    public static bool IsNoIconSubjectImage(string? url)
    {
        return string.Equals(Normalize(url), NoIconSubjectImageUrl, StringComparison.OrdinalIgnoreCase);
    }
}
