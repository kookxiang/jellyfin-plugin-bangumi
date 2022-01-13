using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class PersonImageProvider : IRemoteImageProvider, IHasOrder
    {
        public int Order => -5;
        public string Name => Constants.ProviderName;

        public bool Supports(BaseItem item)
        {
            return item is Person;
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

            var person = await Api.GetPerson(id, token);

            if (person != null && person.DefaultImage != "")
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Constants.PluginName,
                    Type = ImageType.Primary,
                    Url = person.DefaultImage
                });

            return list;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance!.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}