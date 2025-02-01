using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class MovieId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}
