using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class SubjectImageProvider(BangumiApi api) : IRemoteImageProvider, IHasOrder
{
    public int Order => -5;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Series or Season or Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var id))
            return [];

        var subject = await api.GetSubject(id, cancellationToken);

        if (subject != null && !string.IsNullOrEmpty(subject.DefaultImage))
            return
            [
                new RemoteImageInfo
                {
                    ProviderName = Constants.ProviderName,
                    Type = ImageType.Primary,
                    Url = subject.DefaultImage
                }
            ];

        return [];
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = cancellationToken
        });
    }
}
