using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;

namespace Jellyfin.Plugin.Bangumi.Utils
{
    public static class AnitomyUtil
    {
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
    }
}
