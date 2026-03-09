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
            result ??= await BasicEpisodeParser.SearchEpisodes(context, log, type, subjectId, episodeIndexNumber, false, false);

            if (result != null)
            {
                if (type == EpisodeType.Special || result.Type == EpisodeType.Special)
                {
                    result.SeasonNumber = 0;
                }
                else
                {
                    result.SeasonNumber = seasonNumber;
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
