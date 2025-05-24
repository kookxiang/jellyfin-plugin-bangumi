using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class SeasonNumberId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Season;
    }

    public string ProviderName => Constants.SeasonNumberProviderName;

    public string Key => Constants.SeasonNumberProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

    public string UrlFormatString => "";
}
