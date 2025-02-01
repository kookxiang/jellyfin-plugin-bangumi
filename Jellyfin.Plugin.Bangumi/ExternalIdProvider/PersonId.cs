using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class PersonId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Person;
    }

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderName;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    public string UrlFormatString => "https://bgm.tv/person/{0}";
}
