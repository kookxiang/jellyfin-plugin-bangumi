using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class EpisodePreRefreshProvider : ICustomMetadataProvider<Episode>, IPreRefreshProvider
    {
        public string Name => Constants.ProviderName;

        public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            // 如果原ParentIndexNumber为空，jellyfin会从文件名猜测并预填充值，导致插件识别了也无法修改，因此预先清空该值
            item.ParentIndexNumber = null;

            return Task.FromResult(ItemUpdateType.None);
        }
    }
}
