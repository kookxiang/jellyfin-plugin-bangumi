using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
#if !EMBY
using Jellyfin.Data.Enums;
#endif

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class RatingRefreshTask(ILibraryManager library, BangumiApi api)
    : IScheduledTask
{
    public string Key => "RatingRefreshTask";
    public string Name => "更新番剧评分";
    public string Description => "更新所有已关联 Bangumi 项目的评分";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

#if EMBY
    public Task Execute(CancellationToken token, IProgress<double> progress)
    {
        var task = Task.Run(async () => await ExecuteAsync(progress, token));
        task.Wait();
        return Task.CompletedTask;
    }
#endif

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        var idList = library.GetItemIds(new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
#if EMBY
                "Movie", "Season", "Series"
#else
                BaseItemKind.Movie, BaseItemKind.Season, BaseItemKind.Series
#endif
            }
        })!;

        var count = 0d;
        foreach (var id in idList)
        {
            // report refresh progress
#if EMBY
            progress.Report(count++ / idList.Length);
#else
            progress.Report(count++ / idList.Count);
#endif

            // check whether current task was canceled
            token.ThrowIfCancellationRequested();

            // obtain library item
            var item = library.GetItemById(id);
            if (item == null) continue;

            // skip item if it was refreshed recently 
#if EMBY
            var dateLastRefreshed = item.DateLastRefreshed.DateTime;
#else
            var dateLastRefreshed = item.DateLastRefreshed;
#endif

            if (DateTime.Now.Subtract(dateLastRefreshed).TotalDays < 14) continue;

            // skip item if it doesn't have bangumi id
            if (!item.ProviderIds.TryGetValue(Constants.ProviderName, out var bangumiId)) continue;

            // limit request speed
            await Task.Delay(TimeSpan.FromSeconds(1), token);

            // get latest rating from bangumi
            var subject = await api.GetSubject(int.Parse(bangumiId!), token);
            var score = subject?.Rating?.Score;
            if (score == null) continue;

            // skip saving item if it's rating is already up to date
            if (item.CommunityRating != null && Math.Abs((float)(item.CommunityRating! - score)) < 0.1) continue;

            // save item
            item.CommunityRating = score;
#if EMBY
            library.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataDownload);
#else
            await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataDownload, token);
#endif
        }
    }
}