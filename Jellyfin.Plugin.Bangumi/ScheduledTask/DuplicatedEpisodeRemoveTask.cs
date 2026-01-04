using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class DuplicatedEpisodeRemoveTask(Logger<DuplicatedEpisodeRemoveTask> logger, ILibraryManager library) : IScheduledTask
{
    public string Key => "DuplicatedEpisodeRemoveTask";
    public string Name => "重复剧集清理";
    public string Description => "清理重复 Bangumi ID 的剧集";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new()
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
        }
    ];

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public void Execute(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Configuration.RemoveDuplicatedEpisode == RemoveDuplicatedEpisodeMode.Off) return;

        progress.Report(0);

        Dictionary<string, List<BaseItem>> map = new();

        var query = new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Episode] };
        var episodeList = library.GetItemList(query)
            .Where(o => o.ProviderIds.ContainsKey(Constants.PluginName))
            .Distinct()
            .ToList();

        logger.Info($"Found {episodeList.Count} episodes with Bangumi IDs.");

        progress.Report(10);

        foreach (var episode in episodeList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = episode.GetProviderId(Constants.PluginName);
            if (id is null) continue;
            if (!map.ContainsKey(id)) map[id] = [];
            map[id].Add(episode);
        }

        progress.Report(20);

        int current = 0;
        foreach (var (id, list) in map)
        {
            progress.Report(20 + current * 80D / map.Count);
            current++;
            cancellationToken.ThrowIfCancellationRequested();
            if (list.Count <= 1)
            {
                logger.Debug("Episode with Bangumi ID {Id} has no duplicates.", id);
                continue;
            }

            logger.Info("Episode with Bangumi ID {Id} has {Count} copies.", id, list.Count);
            List<BaseItem> pendingRemovalList;

            if (Configuration.RemoveDuplicatedEpisode is RemoveDuplicatedEpisodeMode.ModifiedTime)
            {
                var paths = list.Select(item => item.Path).OrderByDescending(path => new FileInfo(path).LastWriteTime).ToList();
                pendingRemovalList = list.Where(item => item.Path != paths[0]).ToList();
            }
            else
            {
                throw new NotImplementedException($"RemoveDuplicatedEpisodeMode {Configuration.RemoveDuplicatedEpisode} not implemented.");
            }

            if (pendingRemovalList.Count <= 0) continue;

            // Make sure each item has similar time length
            double avgTicks = pendingRemovalList.Select(x => x.RunTimeTicks).Where(x => x != null).Average() ?? 0;
            foreach (var baseItem in pendingRemovalList)
            {
                if (baseItem.RunTimeTicks == null)
                {
                    logger.Warn("Episode {Name} ({Id}) from {Path} has no RunTimeTicks info, skipped.", baseItem.Name, baseItem.Id, baseItem.Path);
                    continue;
                }

                var diff = Math.Abs(baseItem.RunTimeTicks.Value - avgTicks);
                if (diff > Math.Max(TimeSpan.FromMinutes(1).Ticks, avgTicks! * 0.01D))
                {
                    logger.Warn("Episode {Name} ({Id}) from {Path} length differs too much from average, skipped.", baseItem.Name, baseItem.Id, baseItem.Path);
                    continue;
                }
            }

            logger.Info("Removing duplicated episode {Id} files: {Paths}", id, string.Join(", ", pendingRemovalList.Select(item => item.Path)));
            foreach (var baseItem in pendingRemovalList)
            {
                library.DeleteItem(baseItem, new DeleteOptions { DeleteFileLocation = true }, true);
            }
        }
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        this.Execute(progress, cancellationToken);
        return Task.CompletedTask;
    }
}
