using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Extension;

public class ExternalBookId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Book;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

    public string UrlFormatString => "https://bgm.tv/subject/{0}";
}