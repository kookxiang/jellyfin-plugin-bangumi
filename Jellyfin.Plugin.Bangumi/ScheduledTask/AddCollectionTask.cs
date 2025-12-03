using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;


namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

/// <summary>
/// inspired by https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult
/// </summary>
public class AddCollectionTask(BangumiApi api, ILibraryManager libraryManager, ICollectionManager collectionManager, Logger<AddCollectionTask> log) : IScheduledTask
{
    public string Key => Constants.PluginName + "AddCollectionTask";

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
        var query = new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie] };
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
            var bangumiSeriesIds = await api.GetAllAnimeSeriesSubjectIds(int.Parse(providerIds), cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            // 取出在 subjects 中出现的所有 id
            var subjectsInLibrary = subjects
                .Where(o => bangumiSeriesIds.Contains(int.Parse(o.ProviderIds.GetValueOrDefault(Constants.PluginName) ?? "-1")))
                .Distinct()
                .ToList();

            // 跳过数量小于 2 的
            if (subjectsInLibrary.Count < 2)
                continue;

            // 使用系列中最小 id 对应的名字作为合集名
            // FIXME 不一定是第一部，与 Bangumi 数据录入先后有关
            var firstSeries = await api.GetSubject(bangumiSeriesIds.Min(), cancellationToken);
            if (firstSeries is null)
                continue;

            // 创建合集
            var option = new CollectionCreationOptions
            {
                Name = $"{(string.IsNullOrEmpty(firstSeries.ChineseName) ? firstSeries.OriginalName : firstSeries.ChineseName)}（系列）",
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

            // 添加已处理的 subjects，避免后面重复处理
            processed.UnionWith(subjectsInLibrary.Select(c => c.Id));
            log.Info("添加合集：{subjects}", string.Join(", ", subjectsInLibrary.Select(s => s.Name)));

            // 随机设置合集封面
            var moviesImages = subjectsInLibrary.Where(o => o.HasImage(ImageType.Primary));
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

internal static class EnumerableExtension
{
    public static IEnumerable<(int index, T item)> WithIndex<T>(this IEnumerable<T> source)
        => source.Select((item, index) => (index, item));

    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        var r = new Random();
        var list = enumerable as IList<T> ?? enumerable.ToList();

        return list.ElementAt(r.Next(0, list.Count));
    }
}
