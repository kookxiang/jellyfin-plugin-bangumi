using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.BasicParser;
using Jellyfin.Plugin.Bangumi.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, ILoggerFactory loggerFactory)
    : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Virtual episodes are maintained from the archive by MissingEpisodeProvider.
        // They have no file to parse and must never trigger an online metadata fallback.
        if (string.IsNullOrEmpty(info.Path))
            return new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        using var refreshScope = BangumiApi.BeginRequestedRefresh(info.Path);
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        var context = new EpisodeParserContext(api, libraryManager, info, mediaSourceManager, Configuration, localConfiguration, cancellationToken);
        var parser = EpisodeParserFactory.CreateParser(Configuration, context, loggerFactory);

        Model.Episode? episode = null;

        // throw execption will cause the episode to not show up anywhere
        try
        {
            episode = BangumiApi.IsFreshMetadataRefresh
                && int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var savedId) && savedId > 0
                ? await api.GetEpisode(savedId, cancellationToken)
                : await parser.GetEpisode();

            log.Info("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);
        }
        catch (Exception e)
        {
            log.Error($"metadata for {info.Path} error: {e.Message}");
        }

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (localConfiguration.Skip) return result;

        var forcedType = localConfiguration.GetForcedEpisodeType();
        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);

        if (episode == null)
        {
            // remove season number
            if (forcedType != null)
            {
                result.HasMetadata = true;
                result.Item = new Episode
                {
                    ParentIndexNumber = (await ResolveSeasonNumberAsync(episode, parent, info, forcedType, localConfiguration, cancellationToken)).seasonNumber
                };
            }
            else if (BasicEpisodeParser.IsSpecial(info.Path, context.LibraryManager, true))
            {
                result.HasMetadata = true;
                result.Item = new Episode { ParentIndexNumber = 0 };
            }

            return result;
        }

        result.Item = new Episode();
        result.HasMetadata = true;
        if (episode.Id != 0)
            result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");

        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        result.Item.Name = episode.Name;
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = LocalConfigurationHelper.GetDisplayEpisodeIndex(episode.Order, localConfiguration, info.Path);
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

        var (seasonNumber, seasonId) = await ResolveSeasonNumberAsync(episode, parent, info, forcedType, localConfiguration, cancellationToken);
        result.Item.ParentIndexNumber = seasonNumber;
        if (seasonId != Guid.Empty) result.Item.SeasonId = seasonId;

        if (result.Item.ParentIndexNumber is not 0)
        {
            FillFallbackTitle(result.Item, info);
            return result;
        }

        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, cancellationToken);

        if (series == null)
        {
            FillFallbackTitle(result.Item, info);
            return result;
        }

        if (string.IsNullOrEmpty(result.Item.Name) || string.IsNullOrEmpty(result.Item.OriginalTitle) || string.IsNullOrEmpty(result.Item.Overview))
        {
            var episodes = await api.GetSubjectEpisodeList(episode.ParentId, null, episode.Order, cancellationToken);
            if (episodes?.Take(2).Count() == 1)
            {
                if (string.IsNullOrEmpty(result.Item.Name))
                    result.Item.Name = series.Name;
                if (string.IsNullOrEmpty(result.Item.OriginalTitle))
                    result.Item.OriginalTitle = series.OriginalName;
                if (string.IsNullOrEmpty(result.Item.Overview))
                    result.Item.Overview = series.Summary;
            }
        }

        var (airSeasonNumber, _) = await ResolveMainlineSeasonAsync(episode, parent, info, localConfiguration, cancellationToken);
        if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
            result.Item.AirsBeforeSeasonNumber = airSeasonNumber;
        else
            result.Item.AirsAfterSeasonNumber = airSeasonNumber;

        if (episode.Order % 1 != 0)
            result.Item.AirsBeforeEpisodeNumber = (int)Math.Ceiling(episode.Order);

        FillFallbackTitle(result.Item, info);

        return result;
    }

    private static void FillFallbackTitle(Episode item, EpisodeInfo info)
    {
        if (string.IsNullOrEmpty(item.Name))
            item.Name = Path.GetFileNameWithoutExtension(info.Path);
        if (string.IsNullOrEmpty(item.OriginalTitle))
            item.OriginalTitle = Path.GetFileNameWithoutExtension(info.Path);
    }

    // 向上找最近的季：特典目录里可能再套一层普通文件夹（例如 "Season 0/Bonus"），此时直接父级不是 Season
    private Season? FindSeasonParent(BaseItem? parent)
    {
        for (var item = parent; item is not null;)
        {
            if (item is Season season)
                return season;

            if (string.IsNullOrEmpty(item.Path))
                return null;

            var parentPath = Path.GetDirectoryName(item.Path);
            if (string.IsNullOrEmpty(parentPath) || parentPath == item.Path)
                return null;

            item = libraryManager.FindByPath(parentPath, true);
        }

        return null;
    }

    private async Task<(int? seasonNumber, Guid seasonId)> ResolveSeasonNumberAsync(
    Model.Episode? episode,
    BaseItem? parent,
    EpisodeInfo info,
    EpisodeType? forcedType,
    LocalConfiguration localConfiguration,
    CancellationToken token)
    {
        // forcedType
        if (forcedType == EpisodeType.Special)
            return (0, Guid.Empty);
        if (forcedType == EpisodeType.Normal)
            return GetNormalSeasonNumber();

        // 特典
        if (episode?.SeasonNumber == 0)
            return (0, Guid.Empty);

        // special
        var isNormal = (forcedType ?? episode?.Type) == EpisodeType.Normal;
        if (!isNormal
            || episode?.Type == EpisodeType.Special
            || BasicEpisodeParser.IsSpecial(info.Path, libraryManager, true))
            return (0, Guid.Empty);

        // 物理 Season 0（包括特典目录里再套一层文件夹的情况）
        if (FindSeasonParent(parent) is { IndexNumber: 0 } season)
            return (0, season.Id);

        return await ResolveMainlineSeasonAsync(episode, parent, info, localConfiguration, token);

        (int, Guid) GetNormalSeasonNumber()
        {
            if (FindSeasonParent(parent) is { IndexNumber: > 0 } season)
                return (season.IndexNumber.GetValueOrDefault(1), season.Id);
            if (episode?.SeasonNumber > 0)
                return (Math.Max(1, (int)episode.SeasonNumber.Value), Guid.Empty);
            return (info.ParentIndexNumber > 0 ? info.ParentIndexNumber.Value : 1, Guid.Empty);
        }
    }

    private async Task<(int? seasonNumber, Guid seasonId)> ResolveMainlineSeasonAsync(
    Model.Episode? episode,
    BaseItem? parent,
    EpisodeInfo info,
    LocalConfiguration localConfiguration,
    CancellationToken token)
    {
        // 最近的季（季目录里可能还套着普通文件夹）
        var season = FindSeasonParent(parent);
        if (season is { IndexNumber: > 0 } numberedSeason)
            return (numberedSeason.IndexNumber!.Value, numberedSeason.Id);

        // relation chain
        var seriesId = episode is { ParentId: > 0 }
            ? episode.ParentId
            : LocalConfigurationHelper.GetSeriesId(localConfiguration, info, libraryManager);
        if (Configuration.UseBangumiRelationChainForEpisodeSeasonNumber
            && await SeasonHelper.TryResolveChainSeasonNumberAsync(api, seriesId, info.SeriesProviderIds, log, token) is { } chainSeasonNumber)
            return (chainSeasonNumber, Guid.Empty);

        // parser result
        if (episode?.SeasonNumber > 0)
            return ((int)episode.SeasonNumber.Value, Guid.Empty);

        // season default: null
        if (season is not null)
            return (info.ParentIndexNumber is > 0 ? info.ParentIndexNumber.Value : null, season.Id);

        // normal
        return (parent is Series ? 1 : (info.ParentIndexNumber is > 0 ? info.ParentIndexNumber.Value : 1), Guid.Empty);
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }
}
