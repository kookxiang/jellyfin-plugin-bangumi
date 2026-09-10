using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalConfiguration = Jellyfin.Plugin.Bangumi.Model.LocalConfiguration;
using Jellyfin.Plugin.Bangumi.Resolvers;
using MediaBrowser.Model.Providers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class EpisodePreRefreshProvider : ICustomMetadataProvider<Episode>, IPreRefreshProvider
    {
        // Lookup info is created before pre-refresh providers run. Apply the same correction
        // to the remote provider's input as well as the persisted item.
        internal static bool CorrectEpisodeNumbers(EpisodeInfo info, LocalConfiguration configuration)
        {
            if (configuration.Skip)
                return false;
            var identity = BangumiEpisodeVersionResolver.GetFileIdentity(info.Path, configuration);
            if (identity is not { } value || value.Number > int.MaxValue
                || decimal.Truncate(value.Number) != value.Number)
                return false;

            info.IndexNumber = (int)value.Number;
            info.IndexNumberEnd = null;
            info.ParentIndexNumber = value.Type == Model.EpisodeType.Normal ? value.Season : 0;
            return true;
        }

        public string Name => Constants.ProviderName;

        public async Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            // 如果原ParentIndexNumber为空，jellyfin会从文件名猜测并预填充值，导致插件识别了也无法修改，因此预先清空该值
            item.ParentIndexNumber = null;

            if (Plugin.Instance?.Configuration.MergeEpisodeVersionsByBangumiId == true)
            {
                var configuration = await LocalConfiguration.ForPath(item.Path);
                var info = new EpisodeInfo { Path = item.Path };
                if (CorrectEpisodeNumbers(info, configuration))
                {
                    item.IndexNumber = info.IndexNumber;
                    item.IndexNumberEnd = null;
                    item.ParentIndexNumber = info.ParentIndexNumber;
                    return ItemUpdateType.MetadataImport;
                }
            }

            return ItemUpdateType.None;
        }
    }
}
