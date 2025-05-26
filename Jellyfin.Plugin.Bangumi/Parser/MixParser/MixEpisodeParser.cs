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
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Bangumi.Parser.MixParser
{
    public partial class MixEpisodeParser(EpisodeParserContext context, Logger<MixEpisodeParser> log) : IEpisodeParser
    {
        public async Task<Model.Episode?> GetEpisode()
        {
            Model.Episode? result = null;

            // 如果是杂项文件，跳过搜索
            if (IsMisc(context.Info.Path))
            {
                log.Info($"misc file match, skip getting metadata: {context.Info.Path}");

                // 清除之前获取的元数据
                result = new Model.Episode();
                return result;
            }

            var fileName = Path.GetFileName(context.Info.Path);
            var type = IsSpecial(context.Info.Path) ? EpisodeType.Special : EpisodeType.Normal;
            // 如果是特典文件，固定季号为0
            if (type == EpisodeType.Special)
            {
                result = new Model.Episode()
                {
                    ParentIndexNumber = 0,
                };
            }

            if (string.IsNullOrEmpty(fileName))
                return result;

            // 从元数据中获取已识别的Subject ID
            var subjectId = BasicEpisodeParser.GetSubjectId(context, log);
            // 否则尝试通过番剧名称搜索
            if (subjectId > 0)
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
                ?? await BasicEpisodeParser.SearchEpisodes(context, log, type, subjectId, episodeIndexNumber);
            if (result != null)
            {
                if (type == EpisodeType.Special || result.Type == EpisodeType.Special)
                {
                    result.ParentIndexNumber = 0;
                }
            }

            return result;
        }

        public static double? ExtractSeasonNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return AnitomyEpisodeParser.ExtractSeasonNumberFromPath(context, log);
        }

        public static double? ExtractEpisodeNumberFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            var num = AnitomyEpisodeParser.ExtractEpisodeNumberFromPath(context, log);
            num ??= BasicEpisodeParser.ExtractEpisodeNumberFromPath(context, log);

            return num;
        }

        public static string? ExtractAnimeTitleFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            return AnitomyEpisodeParser.ExtractAnimeTitleFromPath(context, log);
        }

        public static EpisodeType? ExtractEpisodeTypeFromPath<T>(EpisodeParserContext context, Logger<T> log)
        {
            var type = AnitomyEpisodeParser.ExtractEpisodeTypeFromPath(context, log);
            if (type != null)
            {
                return type;
            }

            return BasicEpisodeParser.ExtractEpisodeTypeFromPath(context, log);
        }

        /// <summary>
        /// 检查文件路径是否为特典文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否为特典文件</returns>
        private bool IsSpecial(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            // 匹配完整路径
            bool result = PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.SpExcludeRegexFullPath,
                filePath,
                (p, e) => log.Error($"Check if filePath \"{filePath}\" is special episode using regex \"{p}\" failed:  {e.Message}"));
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
        private bool IsMisc(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            // 匹配完整路径
            bool result = PluginConfiguration.MatchExcludeRegexes(
                Plugin.Instance!.Configuration.MiscExcludeRegexFullPath,
                filePath,
                (p, e) => log.Error($"Check if filePath \"{filePath}\" is misc file using regex \"{p}\" failed:  {e.Message}"));
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
