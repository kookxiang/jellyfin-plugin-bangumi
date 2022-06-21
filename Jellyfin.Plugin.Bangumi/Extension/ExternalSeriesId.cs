using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalSeriesId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Series;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.PluginName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}