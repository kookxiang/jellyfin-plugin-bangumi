using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
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

        private static readonly Regex[] SpecialEpisodeFileNameRegex = { new(@"Special"), new(@"OVA") };

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
            Model.Episode? episode = null;
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
                if (episode != null)
                    if (!SpecialEpisodeFileNameRegex.Any(x => x.IsMatch(info.Path)))
                        if ($"{episode?.ParentId}" != seriesId)
                        {
                            _log.LogWarning($"episode #{episodeId} is not belong to series #{seriesId}, ignored");
                            episode = null;
                        }
            }

            if (episode == null)
            {
                var originalEpisodeIndex = info.IndexNumber ?? 0;
                var episodeIndex = originalEpisodeIndex;

                foreach (var regex in EpisodeFileNameRegex)
                {
                    if (!regex.IsMatch(fileName))
                        continue;
                    episodeIndex = int.Parse(regex.Match(fileName).Groups[1].Value);
                    break;
                }

                var episodeListData = await Api.GetSubjectEpisodeList(seriesId, token);
                if (episodeListData?.Data == null)
                    return result;

                if (originalEpisodeIndex > episodeListData.Data.Max(ep => ep.Order))
                {
                    _log.LogWarning($"file {fileName} has incorrect episode index {originalEpisodeIndex}, set to {episodeIndex}");
                }
                else if (episodeIndex > 0 && originalEpisodeIndex <= 0)
                {
                    _log.LogWarning($"file {fileName} may has incorrect episode index {originalEpisodeIndex}, should be {episodeIndex}");
                }
                else
                {
                    _log.LogInformation($"use exists episode number {originalEpisodeIndex} from file name {fileName}");
                    episodeIndex = originalEpisodeIndex;
                }

                episode = episodeListData.Data.Find(x => x.Type == EpisodeType.Normal && (int)x.Order == episodeIndex);
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
            result.Item.OriginalTitle = episode.OriginalName;
            result.Item.IndexNumber = (int)episode.Order;
            result.Item.Overview = episode.Description;

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
        {
            var httpClient = Plugin.Instance!.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }
    }
}