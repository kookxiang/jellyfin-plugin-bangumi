using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILogger _log;
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public EpisodeProvider(BangumiApi api, ILogger log)
    {
        _api = api;
        _log = log;
    }

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var episode = await GetEpisode(info, token);

        _log.Info("metadata for {File}: {EpisodeInfo}", info.Name, episode);

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (episode == null)
            return result;

        result.Item = new Episode();
        result.HasMetadata = true;
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");

        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        result.Item.Name = episode.GetName(Plugin.Instance!.Configuration);
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;
        result.Item.ParentIndexNumber = info.ParentIndexNumber ?? 1;

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // mark episode as special
        result.Item.ParentIndexNumber = 0;

        // use title and overview from special episode subject if episode data is empty
        var series = await _api.GetSubject(episode.ParentId, token);
        if (series == null)
            return result;

        // use title and overview from special episode subject if episode data is empty
        if (string.IsNullOrEmpty(result.Item.Name))
            result.Item.Name = series.GetName(Plugin.Instance.Configuration);
        if (string.IsNullOrEmpty(result.Item.OriginalTitle))
            result.Item.OriginalTitle = series.OriginalName;
        if (string.IsNullOrEmpty(result.Item.Overview))
            result.Item.Overview = series.Summary;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken token)
    {
        return _api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = token
        });
    }

    private async Task<Model.Episode?> GetEpisode(EpisodeInfo info, CancellationToken token)
    {
        if (!int.TryParse(info.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out var seriesId))
            return null;

        double? episodeIndex = info.IndexNumber;

        if (episodeIndex is null)
            return null;

        if (int.TryParse(info.GetProviderId(Constants.ProviderName), out var episodeId))
        {
            var episode = await _api.GetEpisode(episodeId, token);
            if (episode == null)
                goto SkipBangumiId;

            if (Configuration.TrustExistedBangumiId)
                return episode;

            if (episode.ParentId == seriesId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                return episode;
        }

    SkipBangumiId:
        var episodeListData = await _api.GetSubjectEpisodeList(seriesId, null, episodeIndex.Value, token);
        if (episodeListData == null)
            return null;
        try
        {
            return episodeListData.OrderBy(x => x.Type).First(x => x.Order.Equals(episodeIndex));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}