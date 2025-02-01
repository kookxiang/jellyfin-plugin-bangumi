using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class AlbumId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is MusicAlbum;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Album;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}
