using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Parser;

public static class LocalConfigurationHelper
{
    public static Model.Episode? MatchDirectoryEpisode(IEnumerable<Model.Episode> episodes, EpisodeType type, double index)
    {
        var ordered = episodes.OrderBy(e => e.Type == type ? 0 : 1).ThenBy(e => e.Type).ToArray();
        var episode = ordered.FirstOrDefault(e => e.Order.Equals(index))
            ?? (index <= 0 && ordered.Length == 1 ? ordered[0] : null);
        // 季号调整只属于当前目录，不修改 API 缓存中可能被其他目录复用的对象。
        return episode?.Copy();
    }

    public static int GetDisplayEpisodeIndex(double order, LocalConfiguration configuration, string? path = null) =>
        configuration.CorrectIndex ? (int)order : (int)order + configuration.ResolveOffset(path);

    /// <summary>
    /// 应用本地配置中的偏移量到剧集索引
    /// </summary>
    /// <param name="episodeIndex"></param>
    /// <param name="localConfiguration"></param>
    public static void ApplyEpisodeOffset(ref double episodeIndex, LocalConfiguration localConfiguration, string? path = null)
    {
        var offset = localConfiguration.ResolveOffset(path);
        if (offset != 0)
            // Applying offset {Offset} to episode index {EpisodeIndex}
            episodeIndex -= offset;
    }

    /// <summary>
    ///     取文件所属季的 Bangumi 条目 ID
    ///     向上找外包着它的季（季里套季）；都找不到才退回系列的条目。
    /// </summary>
    /// <param name="localConfiguration">路径上的本地配置</param>
    /// <param name="info">剧集信息</param>
    /// <param name="libraryManager">媒体库</param>
    /// <returns>Bangumi 条目 ID，取不到返回 0</returns>
    public static int GetSeriesId(LocalConfiguration localConfiguration, EpisodeInfo info, ILibraryManager libraryManager)
    {
        if (localConfiguration.Id != 0)
            // Using local configuration ID {Id} for series ID
            return localConfiguration.Id;

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (parent is not null)
            foreach (var season in parent.GetParents().Prepend(parent).OfType<Season>())
                if (TryGetSubjectId(season, out var seasonId))
                    return seasonId;

        return int.TryParse(info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out var seriesId) ? seriesId : 0;

        static bool TryGetSubjectId(Season season, out int subjectId) =>
            int.TryParse(season.ProviderIds.GetValueOrDefault(Constants.ProviderName), out subjectId) && subjectId > 0;
    }
}
