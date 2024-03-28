using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class ArtistId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is MusicArtist;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Artist;

    public string UrlFormatString => "https://bgm.tv/person/{0}";
}