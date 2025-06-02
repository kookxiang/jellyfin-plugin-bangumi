using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Bangumi.Parser;

public static class LocalConfigurationHelper
{
    /// <summary>
    /// 应用本地配置中的偏移量到剧集索引
    /// </summary>
    /// <param name="episodeIndex"></param>
    /// <param name="localConfiguration"></param>
    public static void ApplyEpisodeOffset(ref double episodeIndex, LocalConfiguration localConfiguration)
    {
        var offset = localConfiguration.Offset;
        if (offset != 0)
            // Applying offset {Offset} to episode index {EpisodeIndex}
            episodeIndex -= offset;
    }

    /// <summary>
    /// 获取本地配置中的系列 ID
    /// </summary>
    /// <param name="localConfiguration"></param>
    /// <param name="info"></param>
    /// <param name="libraryManager"></param>
    /// <returns></returns>
    public static int GetSeriesId(LocalConfiguration localConfiguration, EpisodeInfo info, ILibraryManager libraryManager)
    {
        if (localConfiguration.Id != 0)
            // Using local configuration ID {Id} for series ID
            return localConfiguration.Id;

        var seriesId = 0;
        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (parent is Season && int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
            // Using parent season ID {SeasonId} for series ID
            seriesId = seasonId;
        if (seriesId == 0 && int.TryParse(info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out seriesId))
            // Using series provider ID {SeriesId} for series ID
            return seriesId;

        return seriesId;
    }

}