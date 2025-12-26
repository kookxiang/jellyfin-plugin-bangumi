using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalEpisodeId : IExternalId, IHasWebsite
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Episode;
    }

    public string Name => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public string UrlFormatString => "https://bgm.tv/ep/{0}";
    
    public string Website => "https://bgm.tv";
}
