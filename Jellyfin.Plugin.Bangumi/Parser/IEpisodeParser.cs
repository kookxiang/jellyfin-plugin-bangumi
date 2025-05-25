using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Parser;

public interface IEpisodeParser
{
    Task<Model.Episode?> GetEpisode();

    /// <summary>
    /// 从文件路径中提取季号
    /// </summary>
    static abstract double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log);

    /// <summary>
    /// 从文件路径中提取集号
    /// </summary>
    static abstract double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log);

    /// <summary>
    /// 从文件路径中提取番剧标题
    /// </summary>
    static abstract string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log);

    /// <summary>
    /// 从文件路径中提取剧集类型
    /// </summary>
    static abstract EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log);

    /// <summary>
    /// 从文件路径中分割出Series、Season、Episode的名称
    /// </summary>
    /// <param name="context">上下文</param>
    /// <returns>按索引顺序为 Series、Season、Episode 或 Series、Episode</returns>
    static string[] SplitFilePathParts(EpisodeParserContext context)
    {
        List<string> names = [];

        var path = context.Info.Path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        var directoryName = Path.GetFileName(directory);

        if (context.LibraryManager.FindByPath(directory!, true) is MediaBrowser.Controller.Entities.TV.Season season)
        {
            names.Add(season.SeriesName);
        }
        names.Add(directoryName);
        names.Add(Path.GetFileName(path));

        return [.. names];
    }
}
