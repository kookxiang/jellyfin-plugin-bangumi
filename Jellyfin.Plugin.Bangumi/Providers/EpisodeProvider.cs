using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers
{
    public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private static readonly Regex[] NonEpisodeFileNameRegex =
        {
            new(@"\d{3,4}p"),
            new(@"(Hi)?10p"),
            new(@"(8|10)bit"),
            new(@"(x|h)(264|265)")
        };

        private static readonly Regex[] EpisodeFileNameRegex =
        {
            new(@"\[(\d{2,})\]"),
            new(@"- ?(\d{2,})"),
            new(@"E(\d{2,})"),
            new(@"\[(\d{2,})"),
            new(@"(\d{2,})")
        };

        private static readonly Regex[] SpecialEpisodeFileNameRegex = { new("Special"), new("OVA"), new("OAD") };
        private readonly BangumiApi _api;
        private readonly ILogger<EpisodeProvider> _log;

        private readonly Plugin _plugin;

        public EpisodeProvider(Plugin plugin, BangumiApi api, ILogger<EpisodeProvider> log)
        {
            _plugin = plugin;
            _api = api;
            _log = log;
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
                episode = await _api.GetEpisode(episodeId, token);
                if (episode != null)
                    if (!SpecialEpisodeFileNameRegex.Any(x => x.IsMatch(info.Path)))
                        if ($"{episode.ParentId}" != seriesId)
                        {
                            _log.LogWarning("episode #{Episode} is not belong to series #{Series}, ignored", episodeId, seriesId);
                            episode = null;
                        }
            }

            if (_plugin.Configuration.AlwaysReplaceEpisodeNumber)
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
                var episodeListData = await _api.GetSubjectEpisodeList(seriesId, token);
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

            result.Item.Name = episode.GetName(_plugin.Configuration);
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
            var httpClient = _plugin.GetHttpClient();
            return await httpClient.GetAsync(url, token).ConfigureAwait(false);
        }

        private int GuessEpisodeNumber(int? current, string fileName, double max = double.PositiveInfinity)
        {
            var tempName = fileName;
            var episodeIndex = current ?? 0;
            var episodeIndexFromFilename = episodeIndex;

            foreach (var regex in NonEpisodeFileNameRegex)
            {
                if (!regex.IsMatch(tempName))
                    continue;
                tempName = regex.Replace(tempName, "");
            }

            foreach (var regex in EpisodeFileNameRegex)
            {
                if (!regex.IsMatch(tempName))
                    continue;
                episodeIndexFromFilename = int.Parse(regex.Match(tempName).Groups[1].Value);
                break;
            }

            if (_plugin.Configuration.AlwaysReplaceEpisodeNumber && episodeIndexFromFilename != episodeIndex)
            {
                _log.LogWarning("use episode index {NewIndex} instead of {Index} for {FileName}",
                    episodeIndexFromFilename, episodeIndex, fileName);
                return episodeIndexFromFilename;
            }

            if (episodeIndex > max)
            {
                _log.LogWarning("file {FileName} has incorrect episode index {Index}, set to {NewIndex}",
                    fileName, episodeIndex, episodeIndexFromFilename);
                return episodeIndexFromFilename;
            }

            if (episodeIndexFromFilename > 0 && episodeIndex <= 0)
            {
                _log.LogWarning("file {FileName} may has incorrect episode index {Index}, should be {NewIndex}",
                    fileName, episodeIndex, episodeIndexFromFilename);
                return episodeIndexFromFilename;
            }

            _log.LogInformation("use exists episode number {Index} from file name {FileName}", episodeIndex, fileName);
            return episodeIndex;
        }
    }
}