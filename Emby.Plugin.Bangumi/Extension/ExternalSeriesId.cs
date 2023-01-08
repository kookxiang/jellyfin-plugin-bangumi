using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalSeriesId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Series;
    }

    public string Name => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}