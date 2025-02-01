using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class EpisodeId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Episode;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

    public string UrlFormatString => "https://bgm.tv/ep/{0}";
}
