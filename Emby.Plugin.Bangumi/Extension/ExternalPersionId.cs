using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalPersonId : IExternalId, IHasWebsite
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Person;
    }

    public string Name => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public string UrlFormatString => $"{BangumiApi.BaseWebsiteUrl}/person/{{0}}";
    
    public string Website => BangumiApi.BaseWebsiteUrl;
}
