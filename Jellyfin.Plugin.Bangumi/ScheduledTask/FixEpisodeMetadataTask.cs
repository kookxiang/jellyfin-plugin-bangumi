using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class FixEpisodeMetadataTask(Logger<FixEpisodeMetadataTask> log, ILibraryManager library)
    : IScheduledTask
{
    public string Key => Constants.PluginName + "FixEpisodeMetadataTask";
    public string Name => "修正错误的剧集元数据";
    public string Description => "当前功能：移除剧集中为 0 的 Bangumi ID";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var itemIds = library.GetItemIds(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
        });
        var count = 0d;
        foreach (var itemId in itemIds)
        {
            progress?.Report(100D * count++ / itemIds.Count);

            // add a small delay to reduce resource usage
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

            // check whether current task was canceled
            cancellationToken.ThrowIfCancellationRequested();

            // obtain library item
            var item = library.GetItemById(itemId);
            if (item == null) continue;

            // obtain bangumi episode id
            var bangumiId = item.GetProviderId(Constants.ProviderName);

            // update episode metadata
            if (bangumiId == "0")
            {
                item.ProviderIds.Remove(Constants.ProviderName);
                log.Info("remove ProviderId #{id} for episode {Name}", bangumiId, item.Name);

                // save episode metadata to library
                await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken);
            }
        }
    }
}
