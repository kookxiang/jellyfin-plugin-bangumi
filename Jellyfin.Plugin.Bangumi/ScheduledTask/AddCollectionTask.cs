using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Archive;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;


namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

/// <summary>
/// inspired by https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult
/// </summary>
public class AddCollectionTask(BangumiApi api, ArchiveData archive, ILibraryManager libraryManager, ICollectionManager collectionManager, Logger<AddCollectionTask> log) : IScheduledTask
{
    public string Key => Constants.PluginName + "AddCollectionTask";

    public string Name => "添加合集";

    public string Description => "根据关联条目创建合集";

    public string Category => Constants.PluginName;

    private const int NetworkFallbackThreshold = 100;

#if EMBY
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
    {
        await Task.Yield();
        progress?.Report(0);

        // 获取所有使用 Bangumi 插件的条目（电影/电视剧）
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie],
            IsVirtualItem = false,
            HasAnyProviderId = new Dictionary<string, string>
            {
                { Constants.PluginName, string.Empty }
            },
        };
        IReadOnlyList<BaseItem> subjects = libraryManager.GetItemList(query);

        var subjectMap = new Dictionary<int, List<BaseItem>>();
        foreach (var item in subjects)
        {
            var idStr = item.ProviderIds.GetValueOrDefault(Constants.PluginName);
            if (idStr is null || !int.TryParse(idStr, out var bgmId))
                continue;

            if (!subjectMap.TryGetValue(bgmId, out var list))
            {
                list = [];
                subjectMap[bgmId] = list;
            }
            list.Add(item);
        }
        var total = subjectMap.Count;

        // 条目多时强制要求 archive
        if (total > NetworkFallbackThreshold && !archive.SubjectRelations.Exists())
        {
            log.Error(
                "共 {Count} 个带 Bangumi ID 的条目（> {Threshold}），" +
                "为避免网络请求过多，请配置离线数据库后重试。",
                total, NetworkFallbackThreshold);
            throw new OperationCanceledException("本地 archive 未就绪，任务已取消。");
        }
        log.Info("共 {Count} 个带 Bangumi ID 的条目", total);

        // 已处理的 Bangumi ID，同一系列只需处理一次
        var processedBgmIds = new HashSet<int>();
        var keys = subjectMap.Keys.OrderBy(x => x).ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)i / total * 100);

            var subjectId = keys[i];
            if (processedBgmIds.Contains(subjectId))
                continue;

            // 获取此 id 对应的系列所有 id
            List<int> allBangumiSeriesIds;
            try
            {
                allBangumiSeriesIds = await api.GetAllAnimeSeriesSubjectIds(subjectId, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warn("获取条目 {Id} 关联的所有动画系列失败，跳过该条目。", subjectId);
                continue;
            }

            // 把所有 subject 都标记为已处理
            processedBgmIds.UnionWith(allBangumiSeriesIds);

            // 收集系列中真实存在于库里的条目
            var subjectsInLibrary = new List<BaseItem>();
            foreach (var sid in allBangumiSeriesIds)
            {
                if (subjectMap.TryGetValue(sid, out var group))
                    subjectsInLibrary.AddRange(group);
            }

            if (subjectsInLibrary.Count < 2)
                continue;

            // 系列第一部
            Subject? firstSubject;
            try
            {
                var chain = await api.GetPrequelChainSubjectIds(allBangumiSeriesIds.Min(), cancellationToken);
                log.Debug("bgmId={Id}，系列 {SeriesIds}，第一部 {Root}", subjectId, string.Join(",", allBangumiSeriesIds), chain[0]);
                firstSubject = await api.GetSubject(chain[0], cancellationToken);
                if (firstSubject is null)
                {
                    log.Warn("获取第一部条目 {Id} 合集名失败，跳过该条目。", chain[0]);
                    continue;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warn("获取条目 {Id} 关联的系列第一部失败，跳过该条目。", subjectId);
                continue;
            }



            // 创建合集
            var option = new CollectionCreationOptions
            {
                Name = $"{firstSubject.Name}（系列）",
#if EMBY
                ItemIdList = subjectsInLibrary.Select(o => o.InternalId).ToArray(),
#else
                ItemIdList = subjectsInLibrary.Select(o => o.Id.ToString()).ToArray(),
#endif
            };

#if EMBY
            var collection = await collectionManager.CreateCollection(option).ConfigureAwait(false);
#else
            var collection = await collectionManager.CreateCollectionAsync(option).ConfigureAwait(false);
#endif

            log.Info("添加合集：{subjects}", string.Join(", ", subjectsInLibrary.Select(s => s.Name)));

            // 随机封面
            var imageSources = subjectsInLibrary
                .Select(o => o.GetImageInfo(ImageType.Primary, 0))
                .Where(info => info is not null)
                .ToList();

            if (imageSources.Count > 0)
            {
                collection.SetImage(
                    imageSources[Random.Shared.Next(imageSources.Count)]!, 0);
            }
        }

        progress?.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Enumerable.Empty<TaskTriggerInfo>();
}