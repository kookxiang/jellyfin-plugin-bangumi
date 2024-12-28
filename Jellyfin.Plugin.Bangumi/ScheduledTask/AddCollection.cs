using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;


namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

/// <summary>
/// inspired by https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult
/// </summary>
public class AddCollection(BangumiApi api, ILibraryManager libraryManager, ICollectionManager collectionManager) : IScheduledTask
{
    public string Key => Constants.PluginName + "AddCollection";

    public string Name => "添加合集";

    public string Description => "根据关联条目创建合集";

    public string Category => Constants.PluginName;

#if EMBY
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
    {
        await Task.Yield();
        progress?.Report(0);

        // 获取所有使用 Bangumi 插件的条目（电影/电视剧）
        var query = new InternalItemsQuery { };
        var subjects = libraryManager.GetItemList(query)
            .Where(o => o.ProviderIds.ContainsKey(Constants.PluginName) && (o.GetClientTypeName() == "Series" || o.GetClientTypeName() == "Movie"))
            .Distinct()
            .ToList();

        var processed = new HashSet<Guid>();
        foreach (var (idx, subject) in subjects.WithIndex())
        {
            progress?.Report((double)idx / subjects.Count * 100);

            // 跳过已添加至合集的
            if (processed.Contains(subject.Id))
                continue;

            var providerIds = subject.ProviderIds.GetValueOrDefault(Constants.PluginName);
            if (providerIds is null)
                continue;

            // 获取此 id 对应的系列所有 id
            var bangumiSeriesIds = await api.GetAllSeriesSubjectIds(int.Parse(providerIds), cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            // 取出在 subjects 中出现的所有 id
            var collections = subjects
                .Where(o => bangumiSeriesIds.Contains(int.Parse(o.ProviderIds.GetValueOrDefault(Constants.PluginName) ?? "-1")))
                .Distinct()
                .ToList();

            // 跳过数量小于 2 的
            if (collections.Count < 2)
                continue;

            // 使用系列中最小 id 对应的名字作为合集名
            // 不一定是第一部，与 Bangumi 数据录入先后有关
            var firstSeries = await api.GetSubject(bangumiSeriesIds.Min(), cancellationToken);
            if (firstSeries is null)
                continue;
            
            // 创建合集
            var option = new CollectionCreationOptions
            {
                Name = (firstSeries.ChineseName ?? firstSeries.OriginalName) + "（系列）",
#if EMBY
                ItemIdList = collections.Select(o => o.InternalId).ToArray(),
#else
                ItemIdList = collections.Select(o => o.Id.ToString()).ToArray(),
#endif
            };

#if EMBY
            var collection = await collectionManager.CreateCollection(option).ConfigureAwait(false);
#else
            var collection = await collectionManager.CreateCollectionAsync(option).ConfigureAwait(false);
#endif

            // 添加已处理的 subjects，避免后面重复处理
            processed.UnionWith(collections.Select(c => c.Id));
            Logger.Info("add collection: " + subject.Name+", " + providerIds);

            // 随机设置合集封面
            var moviesImages = collections.Where(o => o.HasImage(ImageType.Primary));
            if (moviesImages.Any())
            {
                collection.SetImage(moviesImages.Random().GetImageInfo(ImageType.Primary, 0), 0);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        progress?.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Enumerable.Empty<TaskTriggerInfo>();
}