using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class SeriesImageProvider : IRemoteImageProvider, IHasOrder
    {
        public int Order => -5;
        public string Name => Constants.ProviderName;

        public bool Supports(BaseItem item)
        {
            return item is Series or Season;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var id = item.GetProviderId(Constants.ProviderName);
            var list = new List<RemoteImageInfo>();

            if (string.IsNullOrEmpty(id))
                return list;

            var subject = await Api.GetSeriesDetail(id, token);

            if (subject.DefaultImage != "")
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = subject.DefaultImage
                });

            return list;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}