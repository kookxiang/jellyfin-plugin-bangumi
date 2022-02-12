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

        private static readonly Regex[] SpecialEpisodeFileNameRegex = { new("Special"), new("OVA"), new("OAD") };

        private readonly ILogger<EpisodeProvider> _log;

        public EpisodeProvider(IApplicationPaths _, ILogger<EpisodeProvider> logger)
        {
            _log = logger;
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
                        if ($"{episode.ParentId}" != seriesId)
                        {
                            _log.LogWarning($"episode #{episodeId} is not belong to series #{seriesId}, ignored");
                            episode = null;
                        }
            }

            if (Plugin.Instance!.Configuration.AlwaysReplaceEpisodeNumber)
            {
                var episodeIndex = GuessEpisodeNumber(info.IndexNumber, fileName);
                if (episodeIndex != info.IndexNumber)
                {
                    info.IndexNumber = episodeIndex;
                    episode = null;
                }
            }

            if (episode == null)
            {
                var episodeListData = await Api.GetSubjectEpisodeList(seriesId, token);
                if (episodeListData?.Data == null)
                    return result;

                var episodeIndex = GuessEpisodeNumber(info.IndexNumber, fileName, episodeListData.Data.Max(ep => ep.Order));
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

        private int GuessEpisodeNumber(int? current, string fileName, double max = double.PositiveInfinity)
        {
            var episodeIndex = current ?? 0;
            var episodeIndexFromFilename = episodeIndex;

            foreach (var regex in EpisodeFileNameRegex)
            {
                if (!regex.IsMatch(fileName))
                    continue;
                episodeIndexFromFilename = int.Parse(regex.Match(fileName).Groups[1].Value);
                break;
            }

            if (Plugin.Instance!.Configuration.AlwaysReplaceEpisodeNumber && episodeIndexFromFilename != episodeIndex)
            {
                _log.LogWarning($"use episode index {episodeIndexFromFilename} instead of {episodeIndex} for {fileName}");
                return episodeIndexFromFilename;
            }

            if (episodeIndex > max)
            {
                _log.LogWarning($"file {fileName} has incorrect episode index {episodeIndex}, set to {episodeIndexFromFilename}");
                return episodeIndexFromFilename;
            }

            if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
            {
                _log.LogWarning($"file {fileName} may has incorrect episode index {episodeIndex}, should be {episodeIndexFromFilename}");
                return episodeIndexFromFilename;
            }

            _log.LogInformation($"use exists episode number {episodeIndex} from file name {fileName}");
            return episodeIndex;
        }
    }
}