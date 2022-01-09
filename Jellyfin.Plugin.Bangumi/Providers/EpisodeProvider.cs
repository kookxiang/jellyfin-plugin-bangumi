using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.API;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private static readonly Regex[] EpisodeFileNameRegex =
        {
            new(@"\[(\d{2,})\]"),
            new(@"- ?(\d{2,})"),
            new(@"E(\d{2,})")
        };

        private readonly ILogger<EpisodeProvider> _log;
        private readonly IApplicationPaths _paths;

        public EpisodeProvider(IApplicationPaths appPaths, ILogger<EpisodeProvider> logger)
        {
            _log = logger;
            _paths = appPaths;
        }

        public int Order => -5;
        public string Name => Constants.ProviderName;

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            API.Episode? episode;
            var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

            var fileName = Path.GetFileName(info.Path);
            if (string.IsNullOrEmpty(fileName))
                return result;

            var seriesId = info.SeriesProviderIds?.GetValueOrDefault(Constants.ProviderName);
            if (string.IsNullOrEmpty(seriesId))
                return result;

            var episodeId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
            if (!string.IsNullOrEmpty(episodeId))
            {
                episode = await Api.GetEpisode(episodeId, token);
            }
            else
            {
                var episodeListData = await Api.GetEpisodeList(seriesId, token);
                if (episodeListData?.Episodes == null)
                    return result;

                var episodeIndex = info.IndexNumber;
                if (episodeIndex > 2 * episodeListData.EpisodeCount)
                {
                    _log.LogWarning($"file {fileName} has incorrect episode index {episodeIndex}, reset to null");
                    episodeIndex = null;
                }
                else if (episodeIndex > episodeListData.EpisodeCount)
                {
                    _log.LogWarning($"file {fileName} may have incorrect episode index {episodeIndex}");
                }

                if (episodeIndex == null)
                {
                    foreach (var regex in EpisodeFileNameRegex)
                    {
                        if (!regex.IsMatch(fileName))
                            continue;
                        episodeIndex = int.Parse(regex.Match(fileName).Groups[1].Value);
                        break;
                    }

                    if (episodeIndex == null)
                        return result;

                    _log.LogInformation($"use episode number {episodeIndex} from file name {fileName}");
                }
                else
                {
                    _log.LogInformation($"use exists episode number {episodeIndex} from file name {fileName}");
                }


                episode = info.ProviderIds?.ContainsKey(Constants.ProviderName) == true
                    ? episodeListData.Episodes.Find(x => $"{x.Id}" == info.ProviderIds[Constants.ProviderName])
                    : episodeListData.Episodes.Find(x => x.Type == EpisodeType.Normal && (int)x.Order == episodeIndex);
            }

            if (episode == null)
                return result;

            result.Item = new Episode();
            result.HasMetadata = true;
            result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");
            if (!string.IsNullOrEmpty(episode.AirDate))
            {
                result.Item.PremiereDate = DateTime.Parse(episode.AirDate);
                result.Item.ProductionYear = DateTime.Parse(episode.AirDate).Year;
            }

            result.Item.Name = episode.Name;
            result.Item.IndexNumber = (int)episode.Order;
            result.Item.Overview = episode.Description;

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}