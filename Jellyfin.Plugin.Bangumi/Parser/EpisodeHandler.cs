using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.Anitomy;
using Jellyfin.Plugin.Bangumi.Parser.Basic;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Parser
{
    public class EpisodeHandler
    {
        private readonly BangumiApi _api;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly PluginConfiguration _configuration;
        private readonly EpisodeInfo _info;
        private readonly LocalConfiguration _localConfiguration;
        private readonly CancellationToken _token;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<EpisodeHandler> _log;

        public EpisodeHandler(BangumiApi api, ILoggerFactory loggerFactory, ILibraryManager libraryManager, PluginConfiguration configuration, EpisodeInfo info, LocalConfiguration localConfiguration, CancellationToken token, IFileSystem fileSystem)
        {
            _api = api;
            _loggerFactory = loggerFactory;
            _libraryManager = libraryManager;
            _configuration = configuration;
            _info = info;
            _localConfiguration = localConfiguration;
            _token = token;
            _fileSystem = fileSystem;
            _log = _loggerFactory.CreateLogger<EpisodeHandler>();
        }
        public async Task<Model.Episode?> GetEpisode()
        {
            var fileName = Path.GetFileName(_info.Path);
            if (string.IsNullOrEmpty(fileName))
                return null;

            var seriesId = GetSeriesId();
            if (seriesId is 0)
                return null;

            var episodeParsers = GetEpisodeParsers();
            if (episodeParsers is null || episodeParsers.Count==0)
                return null;

            double episodeIndex = GetEpisodeIndex(fileName, _info.IndexNumber??0, episodeParsers);

            // 根据 Jellyfin 中配置的元数据 id 获取 episode
            if (_configuration.TrustExistedBangumiId)
            {
                if (int.TryParse(_info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var episodeId) && episodeId != 0)
                {
                    _log.LogInformation("Use episode id {id} in jellyfin config", episodeId);
                    var episode = await _api.GetEpisode(episodeId, _token);
                    if (episode != null)
                        return episode;
                }
            }

            return await GetEpisodeByParser(seriesId, episodeIndex, episodeParsers);
        }

        /// <summary>
        /// 获取系列的 Bangumi ID
        /// </summary>
        /// <returns></returns>
        private int GetSeriesId()
        {
            var seriesId = 0;
            var parent = _libraryManager.FindByPath(Path.GetDirectoryName(_info.Path), true);
            if (parent is Season && int.TryParse(parent.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var seasonId))
                seriesId = seasonId;
            if (seriesId == 0 && int.TryParse(_info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName), out seriesId))
                return seriesId;

            if (_localConfiguration.Id != 0)
                seriesId = _localConfiguration.Id;

            return seriesId;
        }

        /// <summary>
        /// 解析规则
        /// </summary>
        /// <returns></returns>
        private List<IEpisodeParser> GetEpisodeParsers()
        {
            var episodeParsers = new List<IEpisodeParser>();

            episodeParsers.Add(new AnitomyEpisodeParser(_api, _loggerFactory, _libraryManager, _configuration, _info, _localConfiguration, _token, _fileSystem));
            episodeParsers.Add(new BasicEpisodeParser(_api, _loggerFactory, _libraryManager, _configuration, _info, _localConfiguration, _token, _fileSystem));

            return episodeParsers;
        }

        /// <summary>
        /// 获取集数
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="episodeIndex"></param>
        /// <param name="episodeParsers"></param>
        /// <returns></returns>
        private double GetEpisodeIndex(string fileName, double episodeIndex, List<IEpisodeParser> episodeParsers)
        {
            if (_configuration.AlwaysGetEpisodeByAnitomySharp)
            {
                var anitomyEpisodeParser = episodeParsers.OfType<AnitomyEpisodeParser>().First();
                episodeIndex = anitomyEpisodeParser.GetEpisodeIndex(fileName, episodeIndex);
            }
            else if (_configuration.AlwaysReplaceEpisodeNumber || (episodeIndex is 0))
            {
                var basicEpisodeParser = episodeParsers.OfType<BasicEpisodeParser>().First();
                episodeIndex = basicEpisodeParser.GetEpisodeIndex(fileName, episodeIndex);
            }

            ApplyOffset(ref episodeIndex);

            return episodeIndex;
        }

        /// <summary>
        /// 对 episodeIndex 应用配置中的偏移值
        /// </summary>
        /// <param name="episodeIndex"></param>
        private void ApplyOffset(ref double episodeIndex)
        {
            var offset = _localConfiguration.Offset;
            if (offset != 0)
            {
                _log.LogInformation("Applying offset {Offset} to episode index {EpisodeIndex}", -offset, episodeIndex);
                episodeIndex -= offset;
            }
        }

        /// <summary>
        /// 使用解析规则获取剧集
        /// </summary>
        /// <param name="seriesId"></param>
        /// <param name="episodeIndex"></param>
        /// <param name="episodeParsers"></param>
        /// <returns></returns>
        private async Task<Model.Episode?> GetEpisodeByParser(int seriesId, double episodeIndex, List<IEpisodeParser> episodeParsers)
        {
            if (_configuration.AlwaysParseEpisodeByAnitomySharp)
            {
                var anitomyEpisodeParser = episodeParsers.OfType<AnitomyEpisodeParser>().First();
                return await anitomyEpisodeParser.GetEpisode(seriesId, episodeIndex);
            }
            else
            {
                var basicEpisodeParser = episodeParsers.OfType<BasicEpisodeParser>().First();
                return await basicEpisodeParser.GetEpisode(seriesId, episodeIndex);
            }
        }

    }

}
