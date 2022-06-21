using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalMovieId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.PluginName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}