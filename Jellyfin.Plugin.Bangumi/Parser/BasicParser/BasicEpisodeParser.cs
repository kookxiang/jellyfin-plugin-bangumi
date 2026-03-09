using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Bangumi.Parser.BasicParser;

public partial class BasicEpisodeParser(EpisodeParserContext context, Logger<BasicEpisodeParser> log) : IEpisodeParser
{
    private static readonly Regex[] _nonEpisodeFileNameRegex =
    [
        new(@"[\[\(][0-9A-F]{8}[\]\)]", RegexOptions.IgnoreCase),
        new(@"S\d{2,}", RegexOptions.IgnoreCase),
        new(@"yuv[4|2|0]{3}p(10|8)?", RegexOptions.IgnoreCase),
        new(@"\d{3,4}p", RegexOptions.IgnoreCase),
        new(@"\d{3,4}x\d{3,4}", RegexOptions.IgnoreCase),
        new(@"(Hi)?10p", RegexOptions.IgnoreCase),
        new(@"(8|10)bit", RegexOptions.IgnoreCase),
        new(@"(x|h)(264|265)", RegexOptions.IgnoreCase),
        new(@"\d{2,}FPS", RegexOptions.IgnoreCase),
        new(@"\[\d{2}(0[1-9]|1[0-2])(0[1-9]|1[0-9]|2[0-9]|3[0-1])]"),
        new(@"(?<=[^P])V\d+")
    ];

    private static readonly Regex[] _episodeFileNameRegex =
    [
        new(@"\[([\d\.]{2,})\]"),
        new(@"- ?([\d\.]{2,})"),
        new(@"EP?([\d\.]{2,})", RegexOptions.IgnoreCase),
        new(@"第(\d+)巻"),
        new(@"\[([\d\.]{2,})"),
        new(@"#([\d\.]{2,})"),
        new(@"(\d{2,})"),
        new(@"\[([\d\.]+)\]"),
        new(@"^(\d+(\.\d+)?)\.[a-zA-Z]+$")
    ];

    private static readonly Regex[] _allSpecialEpisodeFileNameRegex =
    [
        SpecialEpisodeFileNameRegex(),
        PreviewEpisodeFileNameRegex(),
        OpeningEpisodeFileNameRegex(),
        EndingEpisodeFileNameRegex()
    ];


    [GeneratedRegex(@"(NC)?OP([^a-zA-Z]|$)")]
    private static partial Regex OpeningEpisodeFileNameRegex();

    [GeneratedRegex(@"(NC)?ED([^a-zA-Z]|$)")]
    private static partial Regex EndingEpisodeFileNameRegex();

    [GeneratedRegex(@"(SPs?|Specials?|OVA|OAD)([^a-zA-Z]|$)")]
    private static partial Regex SpecialEpisodeFileNameRegex();

    [GeneratedRegex(@"[^\w]PV([^a-zA-Z]|$)")]
    private static partial Regex PreviewEpisodeFileNameRegex();

    /// <summary>
    /// 检查文件路径是否为特典文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="libraryManager">Jellyfin 媒体库管理器</param>
    /// <param name="checkParent">是否检查父文件夹名称</param>
    /// <returns>是否为特典文件</returns>
    public static bool IsSpecial(string filePath, ILibraryManager libraryManager, bool checkParent = true)
    {
        var fileName = Path.GetFileName(filePath);
        var parentPath = Path.GetDirectoryName(filePath);
        var folderName = Path.GetFileName(parentPath);

        if (checkParent)
        {
            if (parentPath == null)
            {
                checkParent = false;
            }
            else
            {
                // check if parent is a season(subfolder), otherwise it is a series(root folder), check on root folder is not needed
                checkParent = libraryManager.FindByPath(parentPath, true) is Season;
            }
        }

        return SpecialEpisodeFileNameRegex().IsMatch(fileName) ||
               checkParent && SpecialEpisodeFileNameRegex().IsMatch(folderName ?? "");
    }

    public async Task<Model.Episode?> GetEpisode()
    {
        var fileName = Path.GetFileName(context.Info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        // 根据文件路径判断剧集类型：检查文件路径是否为特典文件，否则从文件名猜测类型
        var type = IsSpecial(context.Info.Path, context.LibraryManager) ? EpisodeType.Special : GuessEpisodeTypeFromFileName(fileName);

        // 获取关联的 Bangumi 条目 ID
        var subjectId = LocalConfigurationHelper.GetSeriesId(context.LocalConfiguration, context.Info, context.LibraryManager);

        // 从文件路径中提取集数编号
        double episodeIndex = ExtractEpisodeNumberFromPath(context, log);

        // 应用本地配置中的集数偏移量
        LocalConfigurationHelper.ApplyEpisodeOffset(ref episodeIndex, context.LocalConfiguration);

        // 优先通过已有的 Bangumi 条目 ID 获取剧集，如果未找到则通过搜索剧集列表匹配
        var result = await GetEpisodeFromProviderId(context, log, subjectId, episodeIndex)
            ?? await SearchEpisodes(context, log, type, subjectId, episodeIndex);

        // 特典类的季号统一为 0
        if (result != null
            && (type == EpisodeType.Special || result.Type == EpisodeType.Special))
        {
            result.SeasonNumber = 0;
        }

        return result;
    }

    /// <summary>
    /// 从文件路径中提取集号。
    /// </summary>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <returns>提取到的集号（已应用偏移量），找不到时返回0</returns>
    public static double ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
    {
        var fileName = Path.GetFileName(context.Info.Path);
        if (string.IsNullOrEmpty(fileName))
            return 0;

        // 默认使用 Jellyfin 已有的集数编号
        double episodeIndex = context.Info.IndexNumber ?? 0;

        if (context.Configuration.AlwaysReplaceEpisodeNumber)
        {
            // 勾选了“始终根据文件名猜测集数”
            log.Info("guess episode number from filename {FileName} because of plugin configuration", fileName);
            episodeIndex = GuessEpisodeNumber(context, log, episodeIndex, fileName);
        }
        else if (episodeIndex is 0)
        {
            // 现有集数为空，尝试从文件名中解析
            log.Info("guess episode number from filename {FileName} because it's empty", fileName);
            episodeIndex = GuessEpisodeNumber(context, log, episodeIndex, fileName);
        }

        LocalConfigurationHelper.ApplyEpisodeOffset(ref episodeIndex, context.LocalConfiguration);

        return episodeIndex;
    }

    /// <summary>
    /// 通过已保存的剧集 ID 获取剧集信息。
    /// </summary>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <param name="subjectId">剧集所属的 Bangumi 条目 ID，用于校验剧集信息是否正确</param>
    /// <param name="episodeIndex">期望的集号</param>
    /// <returns>匹配的剧集信息，未找到时返回 null</returns>
    public static async Task<Model.Episode?> GetEpisodeFromProviderId<T>(EpisodeParserContext context, Logger<T> log, int subjectId, double episodeIndex)
    {
        if (int.TryParse(context.Info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
        {
            // 已保存的剧集 ID 存在，尝试直接获取剧集信息
            log.Info("fetching episode info using saved id: {EpisodeId}", episodeId);

            // 通过 API 获取剧集详情
            var episode = await context.Api.GetEpisode(episodeId, context.Token);

            // 找不到剧集信息
            if (episode == null)
                return null;

            // 如果勾选了“始终根据配置的 Bangumi ID 获取元数据”，则直接返回不作进一步校验
            if (context.Configuration.TrustExistedBangumiId)
            {
                log.Info("trust exists bangumi id is enabled, skip further checks");
                return episode;
            }

            // 特别篇（SP/OP/ED/PV）直接返回，无需校验集数匹配
            if (episode.Type != EpisodeType.Normal || _allSpecialEpisodeFileNameRegex.Any(x => x.IsMatch(context.Info.Path)))
            {
                log.Info("current episode is special episode, skip further checks");
                return episode;
            }

            // 校验剧集是否属于当前系列且集数编号匹配
            if (episode.ParentId == subjectId && Math.Abs(episode.Order - episodeIndex) < 0.1)
                return episode;

            log.Info("episode is not belongs to series {SeriesId}, ignoring result", subjectId);
        }

        return null;
    }

    /// <summary>
    /// 在指定系列的剧集列表中搜索匹配的剧集。
    /// </summary>
    /// <param name="context">剧集解析上下文</param>
    /// <param name="log">日志记录器</param>
    /// <param name="type">剧集类型过滤条件，为 null 表示不限类型</param>
    /// <param name="subjectId">所属系列的 Bangumi 条目 ID</param>
    /// <param name="episodeIndex">期望的集号</param>
    /// <param name="guessEpisodeNumber">是否尝试重新猜测集号</param>
    /// <param name="fallback">指定类型未找到匹配时，是否回退到不限类型重新搜索</param>
    /// <returns>匹配的剧集信息，未找到时返回 null</returns>
    public static async Task<Model.Episode?> SearchEpisodes<T>(EpisodeParserContext context, Logger<T> log, EpisodeType? type, int subjectId, double episodeIndex, bool guessEpisodeNumber = true, bool fallback = true)
    {
        var fileName = Path.GetFileName(context.Info.Path);

        // 从 API 获取指定条目的剧集列表，并过滤类型（如果指定了类型）
        log.Info("searching episode in series episode list");
        var episodeListData = await context.Api.GetSubjectEpisodeList(subjectId, type, episodeIndex, context.Token);

        // OVA独立一个条目页面时 API 返回的剧集类型可能为0（正篇内容），导致按特典类型筛选不到结果，此时尝试按正篇类型重新查询
        if ((episodeListData == null || !episodeListData.Any())
            && type == EpisodeType.Special)
        {
            var subject = await context.Api.GetSubject(subjectId, context.Token);
            // 如果条目是OVA类型，尝试按正篇类型重新查询
            if (subject != null &&
                (subject.Platform == SubjectPlatform.OVA || subject.GenreTags.Contains("OVA")))
            {
                episodeListData = await context.Api.GetSubjectEpisodeList(subjectId, EpisodeType.Normal, episodeIndex, context.Token);
            }
        }

        if (episodeListData == null)
        {
            log.Warn("search failed: no episode found in episode");
            return null;
        }

        // 如果仅有一集且为正篇内容，直接返回
        if (episodeListData.Count() == 1 && type is null or EpisodeType.Normal)
        {
            log.Info("only one episode found");
            return episodeListData.First();
        }

        // 对正篇内容，根据剧集列表的最大集数重新猜测文件名中的集数编号
        if (guessEpisodeNumber && type is null or EpisodeType.Normal)
        {
            var maxEpisodeNumber = episodeListData.Any() ? episodeListData.Max(x => x.Order) : double.PositiveInfinity;
            episodeIndex = GuessEpisodeNumber(
                context,
                log,
                episodeIndex + context.LocalConfiguration.Offset,
                fileName,
                maxEpisodeNumber + context.LocalConfiguration.Offset
            ) - context.LocalConfiguration.Offset;
        }

        try
        {
            // 按类型排序后查找匹配集数的剧集，优先匹配正篇
            var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
            if (episode != null || type is null or EpisodeType.Normal)
            {
                log.Info("found matching episode {index} with type {type}", episodeIndex, type);
                return episode;
            }

            if (fallback)
            {
                // 指定类型未找到匹配，回退到不限类型重新搜索
                log.Warn("cannot find episode {index} with type {type}, searching all types", episodeIndex, type);
                return await SearchEpisodes(context, log, null, subjectId, episodeIndex);
            }
            else
            {
                return episode;
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static EpisodeType? GuessEpisodeTypeFromFileName(string fileName)
    {
        var tempName = fileName;
        foreach (var regex in _nonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        if (OpeningEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Opening;
        if (EndingEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Ending;
        if (SpecialEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Special;
        if (PreviewEpisodeFileNameRegex().IsMatch(tempName))
            return EpisodeType.Preview;
        return null;
    }

    private static double GuessEpisodeNumber<T>(EpisodeParserContext context, Logger<T> log, double? current, string fileName, double max = double.PositiveInfinity)
    {
        var tempName = fileName;
        var episodeIndex = current ?? 0;
        var episodeIndexFromFilename = episodeIndex;

        foreach (var regex in _nonEpisodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            tempName = regex.Replace(tempName, "");
        }

        foreach (var regex in _episodeFileNameRegex)
        {
            if (!regex.IsMatch(tempName))
                continue;
            if (!double.TryParse(regex.Match(tempName).Groups[1].Value.Trim('.'), out var index))
                continue;
            episodeIndexFromFilename = index;
            log.Info("used episode number {index} from filename because it matches {pattern}", index, regex);
            break;
        }

        if (context.Configuration.AlwaysReplaceEpisodeNumber)
        {
            log.Warn("use episode number {NewIndex} from filename {FileName}", episodeIndexFromFilename, fileName);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename.Equals(episodeIndex))
        {
            log.Info("use exists episode number {Index} because it's same", episodeIndex);
            return episodeIndex;
        }

        if (episodeIndex > max)
        {
            log.Warn("{FileName} has incorrect episode index {Index} (max {Max}), set to {NewIndex}", fileName, episodeIndex, max, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
        {
            log.Warn("{FileName} may has incorrect episode index {Index}, should be {NewIndex}", fileName, episodeIndex, episodeIndexFromFilename);
            return episodeIndexFromFilename;
        }

        log.Info("use exists episode number {Index}", episodeIndex);
        return episodeIndex;
    }

}
