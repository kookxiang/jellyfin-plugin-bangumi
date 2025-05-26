using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.AnitomyParser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Parser.MixParser;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, Logger<AnitomyEpisodeParser> anitomyLogger, Logger<BasicEpisodeParser> basicLogger, Logger<MixEpisodeParser> mixLogger)
    : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var context = new EpisodeParserContext(api, libraryManager, info, mediaSourceManager, Configuration, localConfiguration, cancellationToken);
        var parser = EpisodeParserFactory.CreateParser(Configuration, context, anitomyLogger, basicLogger, mixLogger);

        Model.Episode? episode = null;

        // throw exception will cause the episode to not show up anywhere
        try
        {
            episode = await parser.GetEpisode();

            log.Info("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);
        }
        catch (Exception e)
        {
            log.Error($"metadata for {info.Path} error: {e.Message}");
        }

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (episode == null)
        {
            return result;
        }

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

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);
        if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            if (season.ProviderIds.TryGetValue(Constants.SeasonNumberProviderName, out var seasonNum))
                if (int.TryParse(seasonNum, out var num))
                    result.Item.ParentIndexNumber = num;
        }

        if (!result.Item.ParentIndexNumber.HasValue)
        {
            result.Item.ParentIndexNumber = (int?)episode.ParentIndexNumber ?? info.ParentIndexNumber;
        }

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, cancellationToken);
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

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return api.GetHttpClient().GetAsync(url, cancellationToken);
    }

}
