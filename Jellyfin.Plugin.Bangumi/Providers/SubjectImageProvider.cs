using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class SubjectImageProvider(BangumiApi api)
    : IRemoteImageProvider, IHasOrder
{
    public int Order => -5;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Series or Season or Movie or Book or MusicAlbum or Audio;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var id))
            return [];

        var imageUrl = await api.GetSubjectImage(id, cancellationToken);

        if (imageUrl != null)
            return
            [
                new RemoteImageInfo
                {
                    ProviderName = Constants.PluginName,
                    Type = ImageType.Primary,
                    Url = imageUrl
                }
            ];

        return [];
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
