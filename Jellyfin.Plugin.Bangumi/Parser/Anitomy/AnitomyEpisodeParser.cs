using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Parser.Anitomy
{
    public class AnitomyEpisodeParser : IEpisodeParser
    {
        private readonly BangumiApi _api;
        private readonly ILogger<AnitomyEpisodeParser> _log;
        private readonly ILibraryManager _libraryManager;
        private readonly PluginConfiguration _configuration;
        private readonly EpisodeInfo _info;
        private readonly LocalConfiguration _localConfiguration;
        private readonly CancellationToken _token;
        private readonly IFileSystem _fileSystem;

        public AnitomyEpisodeParser(BangumiApi api, ILoggerFactory loggerFactory, ILibraryManager libraryManager, PluginConfiguration Configuration, EpisodeInfo info, LocalConfiguration localConfiguration, CancellationToken token, IFileSystem fileSystem)
        {
            _api = api;
            _log = loggerFactory.CreateLogger<AnitomyEpisodeParser>();
            _libraryManager = libraryManager;
            _configuration = Configuration;
            _info = info;
            _localConfiguration = localConfiguration;
            _token = token;
            _fileSystem = fileSystem;
        }


        public async Task<Model.Episode?> GetEpisode(int seriesId, double episodeIndex)
        {
            var fileName = Path.GetFileName(_info.Path);
            var anitomy = new Jellyfin.Plugin.Bangumi.Anitomy(fileName);
            var (anitomyEpisodeType, bangumiEpisodeType) =GetEpisodeType(fileName, anitomy);

            try
            {
                // 获取剧集元数据
                var episodeListData = await _api.GetSubjectEpisodeList(seriesId, bangumiEpisodeType, episodeIndex, _token) ?? new List<Episode>();
                // Bangumi 中本应为`Special`类型的剧集被划分到`Normal`类型的问题
                if (episodeListData.Count == 0 && bangumiEpisodeType is EpisodeType.Special)
                {
                    episodeListData = await _api.GetSubjectEpisodeList(seriesId, null, episodeIndex, _token) ?? new List<Episode>();
                    _log.LogInformation("Process Special: {anitomyEpisodeType} for {fileName}", anitomyEpisodeType, fileName);
                }

                // 匹配剧集元数据
                var episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(episodeIndex));
                if (episode is null)
                {
                    // 该剧集类型下由于集数问题导致无法正确匹配
                    if (bangumiEpisodeType is not null && episodeIndex == 0 && episodeListData.Count != 0)
                    {
                        episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(1));
                    }

                    // 季度分割导致的编号问题
                    // example: Legend of the Galactic Heroes - Die Neue These 12 (48)
                    var episodeIndexAlt = anitomy.ExtractEpisodeNumberAlt();
                    if (episodeIndexAlt is not null)
                    {
                        episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Order.Equals(double.Parse(episodeIndexAlt)));
                    }

                    // 尝试使用 Bangumi `Index`序号进行匹配
                    // if (episodeIndex != 0 && episodeListData.Count != 0)
                    // {
                    //     episode = episodeListData.OrderBy(x => x.Type).FirstOrDefault(x => x.Index.Equals(episodeIndex.Value));
                    // }
                }

                if (episode != null)
                {
                    // 对于无标题的剧集，手动添加标题，而不是使用 Jellyfin 生成的标题
                    if (episode.ChineseNameRaw == "" && episode.OriginalNameRaw == "")
                    {
                        episode.OriginalNameRaw = TitleOfSpecialEpisode(anitomy, anitomyEpisodeType);
                    }
                    return episode;
                }

                // 特典
                var sp = new Jellyfin.Plugin.Bangumi.Model.Episode();
                sp.Type = bangumiEpisodeType ?? EpisodeType.Special;
                sp.Order = episodeIndex;
                sp.OriginalNameRaw = TitleOfSpecialEpisode(anitomy, anitomyEpisodeType);
                _log.LogInformation("Set OriginalName: {OriginalNameRaw} for {fileName}", sp.OriginalNameRaw, fileName);
                return sp;
            }
            catch (InvalidOperationException)
            {
                _log.LogWarning("Error while match episode!");
                return null;
            }
        }

        /// <summary>
        /// 获取剧集类型
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="anitomy"></param>
        /// <returns></returns>
        private (string?, EpisodeType?) GetEpisodeType(string fileName, Bangumi.Anitomy anitomy)
        {
            var (anitomyEpisodeType, bangumiEpisodeType) = AnitomyEpisodeTypeMapping.GetEpisodeType(anitomy.ExtractAnimeType());
            _log.LogDebug("Bangumi episode type: {bangumiEpisodeType}", bangumiEpisodeType);
            // 判断文件夹/ Jellyfin 季度是否为 Special
            if (bangumiEpisodeType is null)
            {
                try
                {
                    string[] parent = { (_libraryManager.FindByPath(Path.GetDirectoryName(_info.Path), true)).Name };
                    // 路径类型
                    var (anitomyPathType, bangumiPathType) = AnitomyEpisodeTypeMapping.GetEpisodeType(parent);
                    // 存在误判的可能性
                    anitomyEpisodeType = anitomyPathType;
                    bangumiEpisodeType = bangumiPathType;
                    _log.LogDebug("Jellyfin parent name: {parent}. Path type: {type}", parent, anitomyPathType);
                }
                catch
                {
                    _log.LogWarning("Failed to get jellyfin parent of {fileName}", fileName);
                }
            }
            return (anitomyEpisodeType, bangumiEpisodeType);
        }

        /// <summary>
        /// 特殊剧集标题
        /// </summary>
        /// <param name="anitomy"></param>
        /// <param name="anitomyEpisodeType"></param>
        /// <returns></returns>
        private string TitleOfSpecialEpisode(Jellyfin.Plugin.Bangumi.Anitomy anitomy, string? anitomyEpisodeType)
        {
            string[] parts = new string[]
                        {
                            anitomy.ExtractAnimeTitle()?.Trim() ?? "",
                            anitomy.ExtractEpisodeTitle()?.Trim() ?? "",
                            anitomyEpisodeType?.Trim() ?? "",
                            anitomy.ExtractEpisodeNumber()?.Trim() ?? ""
                        };
            string separator = " ";
            var titleOfSpecialEpisode = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return titleOfSpecialEpisode;
        }

        public double GetEpisodeIndex(string fileName, double episodeIndex)
        {
            var anitomy = new Jellyfin.Plugin.Bangumi.Anitomy(fileName);
            var anitomyIndex = anitomy.ExtractEpisodeNumber();
            var fileInfo = _fileSystem.GetFileSystemInfo(_info.Path);
            if (!string.IsNullOrEmpty(anitomyIndex))
            {
                episodeIndex = double.Parse(anitomyIndex);
            }
            else if (fileInfo is not null && fileInfo.Length > 100000000)
            {
                // 大于 100MB 的可能是 Movie 等类型
                // 存在误判的可能性，导致被识别为第一集。配合 SP 文件夹判断可降低误判的副作用
                episodeIndex = 1;
                _log.LogDebug("Use episode number: {episodeIndex} for {fileName}, because file size is {size} GB", episodeIndex, fileName, fileInfo.Length / 1000000000);
            }
            else
            {
                // default value
                episodeIndex = 0;
            }
            _log.LogInformation("Use episode number: {episodeIndex} for {fileName}", episodeIndex, fileName);

            // 特典不应用本地配置的偏移值
            var (anitomyEpisodeType, bangumiEpisodeType) = GetEpisodeType(fileName, anitomy);
            if (bangumiEpisodeType is not null or EpisodeType.Normal){
                episodeIndex+= _localConfiguration.Offset;
            }

            return episodeIndex;
        }

    }

}
