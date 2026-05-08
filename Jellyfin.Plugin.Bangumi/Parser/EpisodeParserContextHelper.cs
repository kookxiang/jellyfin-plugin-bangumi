using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Bangumi.Parser
{
    public static class EpisodeParserContextHelper
    {
        /// <summary>
        /// 从文件路径中分割出Series、Season、Episode的名称
        /// </summary>
        /// <param name="context">上下文</param>
        /// <returns>按索引顺序为 Series、Season、Episode 或 Series、Episode</returns>
        public static string[] SplitFilePathParts(EpisodeParserContext context)
        {
            List<string> names = [];

            var path = context.Info.Path;
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            var directoryName = Path.GetFileName(directory);

            if (context.LibraryManager.FindByPath(directory!, true) is Season season)
            {
                names.Add(season.SeriesName);
            }
            names.Add(directoryName);
            names.Add(Path.GetFileName(path));

            return [.. names];
        }
    }
}
