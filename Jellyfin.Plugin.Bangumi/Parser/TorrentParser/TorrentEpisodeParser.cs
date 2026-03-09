using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Bangumi.Parser.TorrentParser
{
    public partial class TorrentEpisodeParser(EpisodeParserContext context, Logger<TorrentEpisodeParser> log) : IEpisodeParser
    {
        public async Task<Model.Episode?> GetEpisode()
        {
            // 如果是杂项文件，跳过搜索
            if (IsMisc(context, log))
            {
                log.Info($"misc file match, skip getting metadata: {context.Info.Path}");

                // 清除之前获取的元数据
                return null;
            }

            var type = ExtractEpisodeTypeFromPath(context, log);
            var seasonNumber = ExtractSeasonNumberFromPath(context, log);
            var result = new Model.Episode();
            // 如果是特典文件，固定季号为0
            if (type == EpisodeType.Special)
            {
                result.SeasonNumber = 0;
            }
            else
            {
                result.SeasonNumber = seasonNumber;
            }

            var fileName = Path.GetFileName(context.Info.Path);
            if (string.IsNullOrEmpty(fileName))
                return result;

            // 从元数据中获取已识别的Subject ID
            var subjectId = GetSubjectId(context, log);
            // 否则尝试通过番剧名称搜索
            if (subjectId <= 0)
            {
                var name = ExtractAnimeTitleFromPath(context, log);
                if (string.IsNullOrEmpty(name)) return result;

                var subjects = await context.Api.SearchSubject(name, context.Token);
                var subject = subjects.FirstOrDefault();
                if (subject != null)
                {
                    subjectId = subject.Id;
                }
            }

            if (subjectId <= 0) return result;

            var episodeIndexNumber = ExtractEpisodeNumberFromPath(context, log) ?? 0;

            // 先置空，方便后面判断是否成功获取到元数据
            result = null;
            // 如果勾选了“始终根据配置的 Bangumi ID 获取元数据”则优先使用已记录的剧集 ID
            if (context.Configuration.TrustExistedBangumiId)
            {
                if (int.TryParse(context.Info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId))
                {
                    // 已保存的剧集 ID 存在，尝试直接获取剧集信息
                    log.Info("fetching episode info using saved id: {EpisodeId}", episodeId);

                    // 通过 API 获取剧集详情
                    result = await context.Api.GetEpisode(episodeId, context.Token);
                }
            }
            // 找不到时，通过集号匹配
            result ??= await SearchEpisodes(context, log, type, subjectId, episodeIndexNumber);

            if (result != null)
            {
                if (type == EpisodeType.Special || result.Type == EpisodeType.Special)
                {
                    result.SeasonNumber = 0;
                }
                else
                {
                    // 此处的季号为偏移值，需要求和
                    if (result.SeasonNumber.HasValue)
                    {
                        result.SeasonNumber = result.SeasonNumber.Value + seasonNumber.GetValueOrDefault(0);
                        // 避免季号小于1
                        result.SeasonNumber = Math.Max(1, result.SeasonNumber.Value);
                    }
                    else
                    {
                        result.SeasonNumber = seasonNumber;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 获取Bangumi番剧id，否则返回0
        /// </summary>
        public static int GetSubjectId<T>(EpisodeParserContext context, Logger<T> log)
        {
            if (context == null) return 0;

            // 从本地配置获取
            var subjectId = context.LocalConfiguration.Id;
            if (subjectId != 0)
            {
                log.Info("get subject id {Id} from local configuration", subjectId);
                return subjectId;
            }

            // 从虚拟Season获取
            if (int.TryParse(context.Info.SeasonProviderIds?.GetValueOrDefault(Constants.ProviderName), out var seasonId) && seasonId != 0)
            {
                log.Info("get subject id {Id} from season provider ids", seasonId);
                return seasonId;
            }

            // 从Season目录获取
            var parent = context.LibraryManager.FindByPath(Path.GetDirectoryName(context.Info.Path)!, true);
            if (parent is Season && int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out seasonId) && seasonId != 0)
            {
                log.Info("get subject id {Id} from parent", seasonId);
                return seasonId;
            }

            // 从Series目录获取
            if (int.TryParse(context.Info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out var seriesId))
            {
                log.Info("get subject id {Id} from series provider ids", seriesId);
                return seriesId;
            }

            log.Warn("cannot find subject id from context, return 0");
            return 0;
        }

        public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return FileNameParser.ExtractAnimeSeason(Path.GetFileName(context.Info.Path), true)
                ?? AnitomyEpisodeParser.ExtractSeasonNumberFromPath(context, log)
                ?? 1;
        }

        public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            var num = FileNameParser.ExtractAnimeEpisodeNumber(Path.GetFileName(context.Info.Path));
            if (num != null)
            {
                double episodeNumber = num.Value;
                LocalConfigurationHelper.ApplyEpisodeOffset(ref episodeNumber, context.LocalConfiguration);
                return episodeNumber;
            }

            num = AnitomyEpisodeParser.ExtractEpisodeNumberFromPath(context, log)
                ?? BasicEpisodeParser.ExtractEpisodeNumberFromPath(context, log);

            return num;
        }

        /// <summary>
        /// 在指定系列的剧集列表中搜索匹配的剧集。
        /// </summary>
        /// <param name="context">剧集解析上下文</param>
        /// <param name="log">日志记录器</param>
        /// <param name="type">剧集类型过滤条件，为 null 表示不限类型</param>
        /// <param name="subjectId">所属系列的 Bangumi 条目 ID</param>
        /// <param name="episodeIndex">期望的集号</param>
        /// <returns>匹配的剧集信息，未找到时返回 null</returns>
        public static async Task<Model.Episode?> SearchEpisodes<T>(EpisodeParserContext context, Logger<T> log, EpisodeType? type, int subjectId, double episodeIndex)
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

            try
            {
                // 按类型排序后查找匹配集数的剧集，优先匹配正篇
                var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
                if (episode != null && (type is null || type == episode?.Type))
                {
                    log.Info("found matching episode {index} with type {type}", episodeIndex, type);
                    return episode;
                }

                var minOrder = episodeListData.Min(x => x.Order);
                var maxOrder = episodeListData.Max(x => x.Order);
                // 如果集号不在剧集列表的范围内，可能是分割放送
                if (type is null or EpisodeType.Normal && episodeIndex < minOrder)
                {
                    // 尝试搜索上一季
                    log.Warn("episode index {index} is less than minimum episode index {MinIndex} in current season, searching nearby seasons", episodeIndex, minOrder);
                    var prev = await context.Api.SearchPreviousSubject(subjectId, 1, context.Token);
                    if (prev != null)
                    {
                        var prevEpisode = await SearchEpisodes(context, log, type, prev.Id, episodeIndex);
                        if (prevEpisode != null)
                        {
                            // 记录季号偏移，用于后续校正
                            prevEpisode.SeasonNumber = prevEpisode.SeasonNumber.GetValueOrDefault(0) - 1;

                            return prevEpisode;
                        }
                    }
                }
                else if (type is null or EpisodeType.Normal && episodeIndex > maxOrder)
                {
                    // 尝试搜索下一季
                    log.Warn("episode index {index} is greater than maximum episode index {MaxIndex} in current season, searching nearby seasons", episodeIndex, maxOrder);
                    var next = await context.Api.SearchNextSubject(subjectId, context.Token);
                    if (next != null)
                    {
                        var nextEpisode = await SearchEpisodes(context, log, type, next.Id, episodeIndex);
                        if (nextEpisode != null)
                        {
                            // 记录季号偏移，用于后续校正
                            nextEpisode.SeasonNumber = nextEpisode.SeasonNumber.GetValueOrDefault(0) + 1;

                            return nextEpisode;
                        }
                    }
                }

                return episode;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return AnitomyEpisodeParser.ExtractAnimeTitleFromPath(context, log);
        }

        public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return IsSpecial(context, log) ? EpisodeType.Special : EpisodeType.Normal;
        }

        /// <summary>
        /// 获取从Series目录开始的文件路径，减少由于媒体库路径导致的误判
        /// </summary>
        /// <param name="filepath">文件路径</param>
        /// <returns></returns>
        private static string GetFilePathFromSeries(EpisodeParserContext context)
        {
            string fullpath = context.Info.Path;

            if (context.LibraryManager.FindByPath(fullpath, false) is MediaBrowser.Controller.Entities.TV.Episode episode)
            {
                if (episode.Series != null)
                {
                    var seriesPath = episode.Series.Path;
                    var libPath = Path.GetDirectoryName(seriesPath);

                    if (!string.IsNullOrEmpty(libPath) && fullpath.StartsWith(libPath))
                    {
                        fullpath = fullpath.Remove(0, libPath.Length);
                    }
                }
            }

            return fullpath;
        }

        /// <summary>
        /// 检查文件路径是否为特典文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否为特典文件</returns>
        private static bool IsSpecial<T>(EpisodeParserContext context, Logger<T> log)
        {
            string filePath = context.Info.Path;
            if (string.IsNullOrEmpty(filePath)) return false;

            // 匹配完整路径
            var fullpath = GetFilePathFromSeries(context);
            bool result = PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.SpExcludeRegexFullPath,
                fullpath,
                (p, e) => log.Error($"Check if filePath \"{fullpath}\" is special episode using regex \"{p}\" failed:  {e.Message}"));
            if (result) return result;

            var parentPath = Path.GetDirectoryName(filePath) ?? "";
            var folderName = Path.GetFileName(parentPath);
            // 匹配文件夹名称
            if (!string.IsNullOrEmpty(folderName))
            {
                // 忽略根目录名称
                if (context.LibraryManager.FindByPath(parentPath, true) is not Series)
                {
                    result |= PluginConfiguration.MatchExcludeRegexes(
                        Plugin.Instance!.Configuration.SpExcludeRegexFolderName,
                        folderName,
                        (p, e) => log.Error($"Check if folderName \"{folderName}\" is special episode using regex \"{p}\" failed:  {e.Message}"));
                    if (result) return result;
                }
            }

            // 匹配文件名
            var fileName = Path.GetFileName(filePath);
            result |= PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.SpExcludeRegexFileName,
                fileName,
                (p, e) => log.Error($"Check if fileName \"{fileName}\" is special episode using regex \"{p}\" failed:  {e.Message}"));

            return result;
        }

        /// <summary>
        /// 检查文件路径是否为杂项文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否为杂项文件</returns>
        private static bool IsMisc<T>(EpisodeParserContext context, Logger<T> log)
        {
            string filePath = context.Info.Path;
            if (string.IsNullOrEmpty(filePath)) return false;

            // 匹配完整路径
            var fullpath = GetFilePathFromSeries(context);
            bool result = PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.MiscExcludeRegexFullPath,
                fullpath,
                (p, e) => log.Error($"Check if filePath \"{fullpath}\" is misc file using regex \"{p}\" failed:  {e.Message}"));
            if (result) return result;

            var parentPath = Path.GetDirectoryName(filePath) ?? "";
            var folderName = Path.GetFileName(parentPath);
            // 匹配文件夹名称
            if (!string.IsNullOrEmpty(folderName))
            {
                // 忽略根目录名称
                if (context.LibraryManager.FindByPath(parentPath, true) is not Series)
                {
                    result |= PluginConfiguration.MatchExcludeRegexes(
                        Plugin.Instance!.Configuration.MiscExcludeRegexFolderName,
                        folderName,
                        (p, e) => log.Error($"Check if folderName \"{folderName}\" is misc file using regex \"{p}\" failed:  {e.Message}"));
                    if (result) return result;
                }
            }

            // 匹配文件名
            var fileName = Path.GetFileName(filePath);
            result |= PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.MiscExcludeRegexFileName,
                fileName,
                (p, e) => log.Error($"Check if fileName \"{fileName}\" is misc file using regex \"{p}\" failed:  {e.Message}"));

            return result;
        }
    }
}
