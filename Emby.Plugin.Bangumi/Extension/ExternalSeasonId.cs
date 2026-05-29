using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalSeasonId : IExternalId, IHasWebsite
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Season;
    }

    public string Name => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public string UrlFormatString => $"{BangumiApi.BaseWebsiteUrl}/subject/{{0}}";
    
    public string Website => BangumiApi.BaseWebsiteUrl;
}
