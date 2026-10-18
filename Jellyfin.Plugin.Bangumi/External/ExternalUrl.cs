using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.External;

public class ExternalUrlProvider : IExternalUrlProvider
{
    public string Name => Constants.ProviderName;

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var id = item.GetProviderId(Constants.ProviderName);
        if (id == null)
            yield break;

        switch (item)
        {
            case MusicAlbum:
            case Book:
            case Movie:
            case Series:
            case Season:
                yield return $"{BangumiApi.BaseWebsiteUrl}/subject/{id}";
                break;
            case Audio:
            case Episode:
                yield return $"{BangumiApi.BaseWebsiteUrl}/ep/{id}";
                break;
            case Person:
            case MusicArtist:
                if (id.StartsWith(Constants.CharacterIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{BangumiApi.BaseWebsiteUrl}/character/{id.Substring(Constants.CharacterIdPrefix.Length)}";
                }
                else
                { yield return $"{BangumiApi.BaseWebsiteUrl}/person/{id}"; }
                break;
            default:
                yield break;
        }
    }
}
