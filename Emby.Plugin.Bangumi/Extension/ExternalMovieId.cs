using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalMovieId : IExternalId, IHasWebsite
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string Name => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";

    public string Website => "https://bgm.tv";
}
