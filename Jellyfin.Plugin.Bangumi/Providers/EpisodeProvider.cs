using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider(BangumiApi api, ILogger<EpisodeProvider> log, ILoggerFactory loggerFactory, ILibraryManager libraryManager, IFileSystem fileSystem)
    : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var localConfiguration = await LocalConfiguration.ForPath(info.Path);
        var basicEpisodeParser = new BasicEpisodeParser(api, info, loggerFactory, libraryManager, fileSystem, token);
        var episode = await basicEpisodeParser.GetEpisode();


        log.LogInformation("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

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
        result.Item.IndexNumber = (int)episode.Order + localConfiguration.Offset;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;
        result.Item.ParentIndexNumber = info.ParentIndexNumber ?? 1;

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (BasicEpisodeParser.IsSpecial(info.Path, false) || episode.Type == EpisodeType.Special || info.ParentIndexNumber == 0)
        {
            result.Item.ParentIndexNumber = 0;
        }
        else if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            if (season.IndexNumber != null)
                result.Item.ParentIndexNumber = season.IndexNumber;
        }

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // mark episode as special
        result.Item.ParentIndexNumber = 0;

        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, token);
        if (series == null)
            return result;

        // use title from special episode subject if episode data is empty
        if (string.IsNullOrEmpty(result.Item.Name))
            result.Item.Name = series.Name;
        if (string.IsNullOrEmpty(result.Item.OriginalTitle))
            result.Item.OriginalTitle = series.OriginalName;

        var seasonNumber = parent is Season ? parent.IndexNumber : 1;
        if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
            result.Item.AirsBeforeEpisodeNumber = seasonNumber;
        else
            result.Item.AirsAfterSeasonNumber = seasonNumber;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return api.GetHttpClient().GetAsync(url, token);
    }
}