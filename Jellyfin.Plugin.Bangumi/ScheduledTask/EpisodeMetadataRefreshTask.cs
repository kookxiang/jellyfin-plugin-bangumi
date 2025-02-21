using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Archive;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class EpisodeMetadataRefreshTask(Logger<EpisodeMetadataRefreshTask> log, ILibraryManager library, ArchiveData archive)
    : IScheduledTask
{
    public string Key => "EpisodeMetadataRefreshTask";
    public string Name => "为近期放送的剧集更新元数据信息";
    public string Description => "从离线数据库中更新近期放送的剧集的元数据信息";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!archive.Episode.Exists()) return;

        var itemIds = library.GetItemIds(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            MinPremiereDate = DateTime.Now.AddMonths(-1),
            MaxPremiereDate = DateTime.Now.AddDays(7)
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
            if (!int.TryParse(item.GetProviderId(Constants.ProviderName) ?? "", out var bangumiId))
            {
                log.Info("item {Name} does not have bangumi id, skipped", item.Name);
                continue;
            }

            // obtain episode metadata
            var episode = (await archive.Episode.FindById(bangumiId))?.ToEpisode();
            if (episode == null)
            {
                log.Info("episode {Id} not found in archive, skipped", bangumiId);
                continue;
            }

            // update episode metadata
            if (!string.IsNullOrEmpty(episode.Name))
                item.Name = episode.Name;
            if (!string.IsNullOrEmpty(episode.OriginalName))
                item.OriginalTitle = episode.OriginalName;
            if (!string.IsNullOrEmpty(episode.Description))
                item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

            log.Info("update metadata for episode #{Id} {Name}", bangumiId, item.Name);

            // save episode metadata to library
            await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken);
        }
    }
}
