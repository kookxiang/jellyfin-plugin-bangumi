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

namespace Jellyfin.Plugin.Bangumi.Parser.MixParser
{
    public partial class MixEpisodeParser(EpisodeParserContext context, Logger<MixEpisodeParser> log) : IEpisodeParser
    {
        public async Task<Model.Episode?> GetEpisode()
        {
            Model.Episode? result = null;

            // 如果是杂项文件，跳过搜索
            if (IsMisc(context, log))
            {
                log.Info($"misc file match, skip getting metadata: {context.Info.Path}");

                // 清除之前获取的元数据
                result = new Model.Episode();
                return null;
            }

            var type = ExtractEpisodeTypeFromPath(context, log);
            var seasonNumber = ExtractSeasonNumberFromPath(context, log);
            result = new Model.Episode();
            // 如果是特典文件，固定季号为0
            if (type == EpisodeType.Special)
            {
                result.ParentIndexNumber = 0;
            }
            else
            {
                result.ParentIndexNumber = seasonNumber;
            }

            var fileName = Path.GetFileName(context.Info.Path);
            if (string.IsNullOrEmpty(fileName))
                return result;

            // 从元数据中获取已识别的Subject ID
            var subjectId = BasicEpisodeParser.GetSubjectId(context, log);
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
            // 获取剧集信息
            result = await BasicEpisodeParser.GetEpisodeFromProviderId(context, log, subjectId, episodeIndexNumber)
                ?? await BasicEpisodeParser.SearchEpisodes(context, log, type, subjectId, episodeIndexNumber, false);
            if (result != null)
            {
                if (type == EpisodeType.Special || result.Type == EpisodeType.Special)
                {
                    result.ParentIndexNumber = 0;
                }
                else
                {
                    result.ParentIndexNumber = seasonNumber;
                }
            }

            return result;
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
                return IEpisodeParser.OffsetEpisodeIndexNumberByLocalConfiguration(context, log, num);
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
        private bool IsMisc<T>(EpisodeParserContext context, Logger<T> log)
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
