using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class MoveSpecialToExtraTask(ILibraryManager libraryManager, Logger<MoveSpecialToExtraTask> log) : IScheduledTask
{

    public string Key => "MoveSpecialToExtraTask";
    public string Name => "将 Specials 移动至花絮";
    public string Description => "将所有 Specials 移动至花絮";
    public string Category => "Bangumi";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // FIXME 移动至花絮后执行刷新会被还原
        // FIXME 只获取 Bangumi 系列
        // 获取所有 Episode
        var episodes = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
        }).Cast<Episode>().ToList();

        log.Info("找到 {Count} 个剧集", episodes.Count);

        var total = episodes.Count;
        var processed = 0;

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;
            progress.Report(processed * 100.0 / total);

            // FIXME 临时限制
            if (!episode.Name.Contains("Blend"))
                continue;

            // 获取父级
            var parent = episode.GetParent();
            if (parent == null)
            {
                log.Info("剧集 {Name} 没有父级，跳过", episode.Name);
                continue;
            }
            // 确保父级是 Season 或 Series
            if (parent is not Season && parent is not Series)
            {
                log.Info("剧集 {Name} 的父级类型为 {Type}，不是 Season 或 Series，跳过", episode.Name, parent.GetType().Name);
                continue;
            }

            // 检查父级是否已包含该 Extra ID
            if (parent.ExtraIds.Contains(episode.Id))
                continue;

            // 设置 ExtraType、OwnerId
            var anitomy = new Anitomy(episode.FileNameWithoutExtension);
            log.Info("episode.FileNameWithoutExtension: {FileNameWithoutExtension}, {episode.Path}", episode.FileNameWithoutExtension, episode.Path);
            var (anitomyEpisodeType, bangumiEpisodeType) = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(anitomy.ExtractAnimeType());
            log.Info("GetAnitomyAndBangumiEpisodeType: {anitomyEpisodeType}, {bangumiEpisodeType}", anitomyEpisodeType, bangumiEpisodeType);
            episode.ExtraType = AnitomyEpisodeTypeMapping.MapToExtraType(anitomyEpisodeType) ?? AnitomyEpisodeTypeMapping.MapToExtraType(bangumiEpisodeType);
            log.Info("episode.ExtraType: {episode.ExtraType}", episode.ExtraType);
            episode.OwnerId = parent.Id;
            log.Info("parent.Id: {parent.Id}", parent.Id);

            // 从文件夹隐藏，内容类型变为继承
            episode.ParentId = new Guid();
            episode.SeasonId = new Guid();
            episode.SeasonName = null;

            // FIXME 以下字段不清楚是否必需
            // episode.SeriesId = new Guid();
            // episode.SeriesName = null;
            // episode.SeriesPresentationUniqueKey = null;
            // episode.ParentIndexNumber = null;
            // episode.IsLocked = true;

            // 跳过没有 ExtraType 的剧集
            if (episode.ExtraType == null)
                continue;

            // 更新父级的 ExtraIds
            var newExtraIds = parent.ExtraIds.Concat(new[] { episode.Id }).Distinct().ToArray();
            parent.ExtraIds = newExtraIds;

            await libraryManager.UpdateItemAsync(episode, parent, ItemUpdateType.MetadataEdit, cancellationToken);
            await libraryManager.UpdateItemAsync(parent, parent.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken);

            log.Info("将 Extra {EpisodeName} (ID: {EpisodeId}) 添加到父级 {ParentName}", episode.Name, episode.Id, parent.Name);
        }
    }
}