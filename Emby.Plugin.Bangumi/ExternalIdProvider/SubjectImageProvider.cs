using System.Collections.Generic;
using System.Linq;
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

public class SubjectImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly BangumiApi _api;

    public SubjectImageProvider(BangumiApi api)
    {
        _api = api;
    }

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Series or Season or Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary };
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var id))
            return Enumerable.Empty<RemoteImageInfo>();

        var subject = await _api.GetSubject(id, token);

        if (subject != null && subject.DefaultImage != "")
            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = Constants.ProviderName,
                    Type = ImageType.Primary,
                    Url = subject.DefaultImage
                }
            };

        return Enumerable.Empty<RemoteImageInfo>();
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken token)
    {
        return _api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = token
        });
    }
}