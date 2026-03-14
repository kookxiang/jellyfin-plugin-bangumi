using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;

public class AnitomyEpisodeParser : IEpisodeParser
{
    private readonly EpisodeParserContext _context;
    private readonly Logger<AnitomyEpisodeParser> _log;
    private readonly string _fileName;
    private readonly Anitomy _anitomy;

    public AnitomyEpisodeParser(EpisodeParserContext parserContext, Logger<AnitomyEpisodeParser> logger)
    {
        _context = parserContext;
        _log = logger;
        _fileName = Path.GetFileName(_context.Info.Path);
        _anitomy = new Anitomy(_fileName);
    }

    public async Task<Episode?> GetEpisode()
    {
        if (string.IsNullOrEmpty(_fileName))
            return null;

        var (anitomyEpisodeType, bangumiEpisodeType) = GetEpisodeType();
        var episodeIndex = GetEpisodeIndex();

        // 获取 seriesId
        var seriesId = LocalConfigurationHelper.GetSeriesId(_context.LocalConfiguration, _context.Info, _context.LibraryManager);
        if (_context.Configuration.ProcessMultiSeasonFolderByAnitomySharp)
            seriesId = await ProcessMultiSeasonFolder(seriesId);

        // 获取 episode
        try
        {
            // 基础规则
            Episode? episode = await BasicRules(seriesId, episodeIndex, anitomyEpisodeType, bangumiEpisodeType);

            // 多季度规则
            // 基础规则未匹配且为普通剧集
            if (episode is null && (bangumiEpisodeType is EpisodeType.Normal || bangumiEpisodeType is null))
            {
                episode = await ProcessMultiSeasonWithConsecutiveIndex(seriesId, episodeIndex);
            }

            // 处理 episode 元数据
            if (episode != null)
            {
                // 处理标题
                // 对于无标题的剧集，手动添加标题，而不是使用 Jellyfin 生成的标题
                if (string.IsNullOrEmpty(episode.ChineseNameRaw) && string.IsNullOrEmpty(episode.OriginalNameRaw))
                {
                    episode.OriginalNameRaw = TitleOfSpecialEpisode(anitomyEpisodeType);
                }
                return episode;
            }

            // 无数据则视为特典
            var sp = new Episode
            {
                Type = bangumiEpisodeType ?? EpisodeType.Special,
                Order = episodeIndex,
                OriginalNameRaw = TitleOfSpecialEpisode(anitomyEpisodeType)
            };
            _log.Info("Set OriginalName: {OriginalNameRaw} for {fileName}", sp.OriginalNameRaw, _fileName);
            return sp;
        }
        catch (InvalidOperationException e)
        {
            _log.Warn("Error while match episode: {message}", e.Message);
            return null;
        }
    }

    /// <summary>
    /// 获取剧集类型
    /// </summary>
    private (string?, EpisodeType?) GetEpisodeType()
    {
        var (anitomyEpisodeType, bangumiEpisodeType) = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(_anitomy.ExtractAnimeType());
        _log.Debug("Bangumi episode type: {bangumiEpisodeType}", bangumiEpisodeType);
        // 判断文件夹/Jellyfin 季度是否为 Special
        if (bangumiEpisodeType is null)
        {
            try
            {
                string[] parent = [_context.LibraryManager.FindByPath(Path.GetDirectoryName(_context.Info.Path)!, true)!.Name];
                // #FIXME 路径类型，存在误判的可能性
                (anitomyEpisodeType, bangumiEpisodeType) = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(parent);
                _log.Debug("Jellyfin parent name: {parent}. Path type: {type}", parent, anitomyEpisodeType);
            }
            catch (Exception e)
            {
                _log.Warn("Failed to get jellyfin parent of {fileName}. {message}", _fileName, e.Message);
            }
        }
        return (anitomyEpisodeType, bangumiEpisodeType);
    }

    /// <summary>
    /// 获取特典剧集标题
    /// </summary>
    /// <param name="anitomyEpisodeType"></param>
    /// <returns></returns>
    private string TitleOfSpecialEpisode(string? anitomyEpisodeType)
    {
        string[] parts =
                    [
                            _anitomy.ExtractAnimeTitle()?.Trim() ?? "",
                            _anitomy.ExtractEpisodeTitle()?.Trim() ?? "",
                            anitomyEpisodeType?.Trim() ?? "",
                            _anitomy.ExtractAnimeSeason()?.Trim()==null? "":"S"+_anitomy.ExtractAnimeSeason()?.Trim(),
                            _anitomy.ExtractVolumeNumber()?.Trim()==null? "":"V"+_anitomy.ExtractVolumeNumber()?.Trim(),
                            _anitomy.ExtractEpisodeNumber()?.Trim()==null? "":"E"+_anitomy.ExtractEpisodeNumber()?.Trim(),
                            _anitomy.ExtractEpisodeNumberAlt()?.Trim()==null? "":"("+_anitomy.ExtractEpisodeNumberAlt()?.Trim()+")"
                    ];
        string separator = " ";
        var titleOfSpecialEpisode = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return titleOfSpecialEpisode;
    }

    /// <summary>
    /// 获取剧集索引
    /// </summary>
    /// <returns></returns>
    private double GetEpisodeIndex()
    {
        double episodeIndex = 0;
        var anitomyIndex = _anitomy.ExtractEpisodeNumber();

        // 直接使用 AnitomySharp 解析的索引
        if (!string.IsNullOrEmpty(anitomyIndex) && double.TryParse(anitomyIndex, out var parsedIndex))
        {
            episodeIndex = parsedIndex;
        }
        else if (_context.Configuration.MovieEpisodeDetectionByAnitomySharp)
        {
            const double MIN_MEDIA_TIME = 10;   // 分钟
            const double MIN_MEDIA_SIZE = 100;   // MB
            var mediaSourceInfo = _context.MediaSourceManager.GetStaticMediaSources(_context.LibraryManager.FindByPath(_context.Info.Path, false), false)?[0];
            if (mediaSourceInfo != null)
            {
                // 视频时长（分钟）
                double mediaTime = TimeSpan.FromTicks(mediaSourceInfo.RunTimeTicks ?? 0).TotalMinutes;
                // 文件大小（MB）
                double mediaSize = (mediaSourceInfo.Size ?? 0) / (1024 * 1024d);
                _log.Debug("Media time: {mediaTime} minutes, Media size: {mediaSize} MB", mediaTime, mediaSize);
                if (mediaTime > MIN_MEDIA_TIME && mediaSize > MIN_MEDIA_SIZE)
                {
                    // 当媒体库中节目和电影混合时，可辅助电影剧集匹配到元数据
                    // 媒体文件时长大于 100 分钟，大小大于 100MB 的可能是 Movie 等类型
                    // 存在误判的可能性，导致被识别为第一集。配合 SP 文件夹判断可降低误判的副作用
                    episodeIndex = 1;
                    _log.Debug("Use episode number: {episodeIndex} for {fileName}, because file size is {size} MB", episodeIndex, _fileName, mediaSize);
                }
            }
        }

        var (_, bangumiEpisodeType) = GetEpisodeType();
        // 特典剧集不应用本地配置的偏移值
        var shouldApplyEpisodeOffset = bangumiEpisodeType is null or EpisodeType.Normal;
        if (shouldApplyEpisodeOffset)
        {
            LocalConfigurationHelper.ApplyEpisodeOffset(ref episodeIndex, _context.LocalConfiguration);
        }

        _log.Info("Use episode number: {episodeIndex} for {fileName}", episodeIndex, _fileName);

        return episodeIndex;
    }

    /// <summary>
    /// 基础规则
    /// </summary>
    /// <param name="seriesId"></param>
    /// <param name="episodeIndex"></param>
    /// <param name="anitomyEpisodeType"></param>
    /// <param name="bangumiEpisodeType"></param>
    /// <returns></returns>
    private async Task<Episode?> BasicRules(int seriesId, double episodeIndex, string? anitomyEpisodeType, EpisodeType? bangumiEpisodeType)
    {
        // 获取剧集元数据
        var episodeListData = await _context.Api.GetSubjectEpisodeList(seriesId, bangumiEpisodeType, episodeIndex, _context.Token) ?? new List<Episode>();

        if (!episodeListData.Any())
        {
            // Bangumi 中本应为`Special`类型的剧集被划分到`Normal`类型的问题
            // 如 OVA 被划为 TV
            if (bangumiEpisodeType is EpisodeType.Special)
            {
                episodeListData = await _context.Api.GetSubjectEpisodeList(seriesId, null, episodeIndex, _context.Token) ?? new List<Episode>();
                _log.Info("Process Special: {anitomyEpisodeType} for {fileName}", anitomyEpisodeType, _fileName);
            }
        }

        // 匹配剧集元数据
        var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));

        if (episode is null)
        {
            // 该剧集类型下由于集数问题导致无法正确匹配，先设置为第一个
            if (bangumiEpisodeType is not null && episodeIndex == 0 && episodeListData.Any())
            {
                return episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(1));
            }

            // 季度分割导致的编号问题
            // example: Legend of the Galactic Heroes - Die Neue These 12 (48)
            var episodeIndexAlt = _anitomy.ExtractEpisodeNumberAlt();
            if (episodeIndexAlt is not null)
            {
                return episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(double.Parse(episodeIndexAlt)));
            }
        }

        return episode;
    }

    /// <summary>
    /// 处理多季度且文件序号连续
    /// 如：「機動戦士ガンダム00」分为两季，每季序号均从1开始，但本地文件命名为 1-50
    /// 如：「らんま1/2」分为两季，第二季序号接第一季顺序，但本地文件命名为 1-161
    /// </summary>
    /// <param name="seriesId"></param>
    /// <param name="episodeIndex"></param>
    /// <returns></returns>
    private async Task<Episode?> ProcessMultiSeasonWithConsecutiveIndex(int seriesId, double episodeIndex)
    {
        var episodeIndexAlt = double.Parse(_anitomy.ExtractEpisodeNumberAlt() ?? "-1");
        // 获取剧集元数据
        var episodeListData = await _context.Api.GetSubjectEpisodeList(seriesId, EpisodeType.Normal, episodeIndex, _context.Token) ?? new List<Episode>();
        var seasonEpisodeCount = episodeListData.Last().Order;
        if (episodeIndex <= seasonEpisodeCount && episodeIndexAlt <= seasonEpisodeCount)
            return null;
        var subjectId = 0;
nextSeason:
// 获取下一季元数据
        var results = await _context.Api.GetRelatedSubjects(seriesId, _context.Token);
        if (results is null)
            return null;
        foreach (var result in results)
        {
            if (result.Relation == SubjectRelation.Sequel)
            {
                subjectId = result.Id;
                _log.Info("use sequel: {sequel} for episode", subjectId);
                break;
            }
        }
        // 无续集
        if (seriesId == subjectId)
            return null;

        seriesId = subjectId;
        episodeListData = await _context.Api.GetSubjectEpisodeList(seriesId, EpisodeType.Normal, episodeIndex, _context.Token) ?? new List<Episode>();
        if (!episodeListData.Any())
            return null;
        // 下一季的下一季……
        // 本季的第一集序号
        var getFirstEpisodeOrder = (await _context.Api.GetSubjectEpisodeList(seriesId, EpisodeType.Normal, 0, _context.Token) ?? new List<Episode>()).First().Order;
        // 本季之前的总序号
        var lastSeasonEpisodeOrder = seasonEpisodeCount;
        // 避免因为缓存导致修改序号时只修改了当前剧集序号，统一调整为：继续排序，方便重新给剧集 Order 重新赋值
        // 第一集 Order 为 1 的直接把所有集数都增加增量
        if (getFirstEpisodeOrder == 1)
        {
            getFirstEpisodeOrder = getFirstEpisodeOrder + seasonEpisodeCount;
            foreach (var e in episodeListData)
            {
                e.Order = e.Order + seasonEpisodeCount;
            }
        }
        // 集数相加，但要减去下一季的第一集序号，然后再加 1，保证多季度序号是否重排或继续排序都不影响正确的序号
        seasonEpisodeCount = lastSeasonEpisodeOrder + episodeListData.Last().Order - getFirstEpisodeOrder + 1;
        if (episodeIndex > seasonEpisodeCount || episodeIndexAlt > seasonEpisodeCount)
            goto nextSeason;

        // 匹配剧集元数据
        var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
        if (episode is null)
        {
            episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex - lastSeasonEpisodeOrder));
        }
        // 重新赋值，保留集数序号，避免多季序号不连续时，同时出现多个第一集
        if (episode is not null)
            episode.Order = episodeIndex;

        if (episode is null && episodeIndexAlt != -1)
        {
            episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndexAlt));
            if (episode is not null)
                episode.Order = episodeIndexAlt;
        }
        if (episode is null && episodeIndexAlt != -1)
        {
            episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndexAlt - lastSeasonEpisodeOrder));
            if (episode is not null)
                episode.Order = episodeIndexAlt;
        }

        return episode;
    }

    /// <summary>
    /// 处理多季度文件夹
    /// 根据文件夹名称搜索，或者使用已存在的 id 
    /// 另外，推荐同时修改季度值
    /// #FIXME 效果一般
    /// </summary>
    /// <param name="seriesId"></param>
    /// <returns></returns>
    private async Task<int> ProcessMultiSeasonFolder(int seriesId)
    {
        // 使用此媒体文件的父目录获取名称
        var parent = _context.LibraryManager.FindByPath(Path.GetDirectoryName(_context.Info.Path)!, true);
        _log.Debug("Jellyfin parent name: {parent}", parent);

        // 配置可跳过处理的文件夹名称
        var skipFolders = new HashSet<string>(
            AnitomyEpisodeTypeMapping.SpecialOther,
            StringComparer.OrdinalIgnoreCase
        );
        skipFolders.UnionWith(["CDs", "Scans", "Bonus"]);
        // 检查是否应跳过处理
        bool shouldSkipFolder = skipFolders.Contains(parent!.Name, StringComparer.OrdinalIgnoreCase) ||
                          parent.Name.Contains('第', StringComparison.OrdinalIgnoreCase) ||
                          parent.Name.Contains("SEASON", StringComparison.OrdinalIgnoreCase);
        if (shouldSkipFolder) return seriesId;

        // 如果在 Jellyfin 中已配置，则直接返回此配置值
        var subjectId = 0;
        _ = int.TryParse(parent.ProviderIds.GetOrDefault(Constants.ProviderName), out subjectId);
        if (subjectId > 0)
        {
            _log.Info("Multi season folder, use exist id: {subjectId}", subjectId);
            return subjectId;
        }

        // 搜索
        var anitomyParent = new Anitomy(parent.Name);
        var searchName = anitomyParent.ExtractAnimeTitle();
        if (searchName is null) return seriesId;
        _log.Info("Multi season folder, Searching {Name} in bgm.tv", searchName);
        var searchResult = await _context.Api.SearchSubject(searchName, _context.Token);
        var animeYear = anitomyParent.ExtractAnimeYear();
        if (animeYear != null)
            searchResult = searchResult.Where(x => x.ProductionYear == animeYear);
        if (searchResult.Any())
        {
            subjectId = searchResult.First().Id;
            _log.Debug("Multi season folder, Use subject id: {id}", subjectId);

            parent.ProviderIds.Add(Constants.ProviderName, subjectId.ToString());
            await _context.LibraryManager.UpdateItemAsync(parent, parent, ItemUpdateType.MetadataEdit, _context.Token);

            // #FIXME 检查与旧 seriesId 的关联性，如果无联系则说明可能匹配错误
            return subjectId;
        }

        return seriesId;
    }

    /// <summary>
    /// 从路径各级名称中提取季号。
    /// </summary>
    /// <typeparam name="T">日志记录器所属类型</typeparam>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <returns>成功时返回季号；否则返回 <see langword="null"/>。</returns>
    public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = EpisodeParserContextHelper.SplitFilePathParts(context);
        // 至少应该包含文件名和一个父级目录
        if (names.Length < 2)
        {
            log.Error("Failed to extract season number from path: {Path}", context.Info.Path);
            return null;
        }

        // 直接从文件名提取季号容易和集号混淆，因此优先从父级提取
        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            if (double.TryParse(anitomy.ExtractAnimeSeason(), out double num))
            {
                return num;
            }
        }
        return null;
    }

    /// <summary>
    /// 从文件名中提取集号，并应用本地配置中的偏移量。
    /// </summary>
    /// <typeparam name="T">日志记录器所属类型</typeparam>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <returns>成功时返回集号；否则返回 <see langword="null"/>。</returns>
    public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        var path = context.Info.Path;
        var filename = Path.GetFileName(path);

        var anitomy = new Anitomy(filename);
        if (double.TryParse(anitomy.ExtractEpisodeNumber(), out double num))
        {
            LocalConfigurationHelper.ApplyEpisodeOffset(ref num, context.LocalConfiguration);
            return num;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// 从路径各级名称中提取番剧名称。
    /// </summary>
    /// <typeparam name="T">日志记录器所属类型</typeparam>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <returns>成功时返回番剧名称；否则返回 <see langword="null"/>。</returns>
    public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = EpisodeParserContextHelper.SplitFilePathParts(context);
        // 至少应该包含文件名和一个父级目录
        if (names.Length < 2)
        {
            log.Error("Failed to extract anime title from path: {Path}", context.Info.Path);
            return null;
        }

        // 文件名可能存在本集标题或只有集号，因此优先从父级提取
        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            var title = anitomy.ExtractAnimeTitle();
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }
        }
        return null;
    }

    /// <summary>
    /// 从路径各级名称中提取剧集类型，并映射为 Bangumi 的剧集类型。
    /// </summary>
    /// <typeparam name="T">日志记录器所属类型</typeparam>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <returns>成功时返回剧集类型；否则返回 <see langword="null"/>。</returns>
    public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        string[] names = EpisodeParserContextHelper.SplitFilePathParts(context);
        // 至少应该包含文件名和一个父级目录
        if (names.Length < 2)
        {
            log.Error("Failed to extract episode type from path: {Path}", context.Info.Path);
            return null;
        }

        foreach (var name in names)
        {
            var anitomy = new Anitomy(name);
            var type = AnitomyEpisodeTypeMapping.GetAnitomyAndBangumiEpisodeType(anitomy.ExtractAnimeType());
            if (type.Item2 != null)
            {
                return type.Item2;
            }
        }
        return null;
    }
}
