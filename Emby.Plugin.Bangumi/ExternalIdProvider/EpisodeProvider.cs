using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.ExternalIdProvider;

public class EpisodeProvider(BangumiApi api, ILogger log) : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var episode = await GetEpisode(info, cancellationToken);

        log.Info("metadata for {0}: {1}", info.Name, episode);

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

        result.Item.Name = episode.Name;
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;
        result.Item.ParentIndexNumber = info.ParentIndexNumber ?? 1;

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // mark episode as special
        result.Item.ParentIndexNumber = 0;

        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, cancellationToken);
        if (series == null)
            return result;

        // use title from special episode subject if episode data is empty
        if (string.IsNullOrEmpty(result.Item.Name))
            result.Item.Name = series.Name;
        if (string.IsNullOrEmpty(result.Item.OriginalTitle))
            result.Item.OriginalTitle = series.OriginalName;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = cancellationToken
        });
    }

    private async Task<Model.Episode?> GetEpisode(EpisodeInfo searchInfo, CancellationToken token)
    {
        var localConfiguration = await LocalConfiguration.ForPath(searchInfo.Path);

        var seasonId = localConfiguration.Id;
        if (seasonId == 0)
            if (!int.TryParse(searchInfo.SeasonProviderIds.GetOrDefault(Constants.ProviderName), out seasonId))
                if (!int.TryParse(searchInfo.SeriesProviderIds.GetOrDefault(Constants.ProviderName), out seasonId))
                    return null;

        double? episodeIndex = searchInfo.IndexNumber;

        if (episodeIndex is null)
            return null;

        var offset = localConfiguration.Offset;
        if (offset != 0)
            episodeIndex -= offset;

        if (int.TryParse(searchInfo.GetProviderId(Constants.ProviderName), out var episodeId))
        {
            var episode = await api.GetEpisode(episodeId, token);
            if (episode == null)
                goto SkipBangumiId;

            if (Configuration.TrustExistedBangumiId)
                return episode;

            if (episode.ParentId == seasonId && Math.Abs(episode.Order - episodeIndex.Value) < 0.1)
                return episode;
        }

        SkipBangumiId:
        var episodeListData = await api.GetSubjectEpisodeList(seasonId, null, episodeIndex.Value, token);
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
