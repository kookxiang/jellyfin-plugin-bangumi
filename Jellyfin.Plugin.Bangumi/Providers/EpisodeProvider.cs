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
using Jellyfin.Plugin.Bangumi.Parser.TorrentParser;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider(BangumiApi api, Logger<EpisodeProvider> log, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, Logger<AnitomyEpisodeParser> anitomyLogger, Logger<BasicEpisodeParser> basicLogger, Logger<TorrentEpisodeParser> torrentLogger)
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
        var parser = EpisodeParserFactory.CreateParser(Configuration, context, anitomyLogger, basicLogger, torrentLogger);

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

        if (localConfiguration.Skip) return result;

        if (episode == null)
        {
            // 清除已有的元数据
            result.Item = new Episode();
            result.HasMetadata = true;

            FillFallbackTitle(result.Item);
            return result;
        }

        result.Item = new Episode();
        result.HasMetadata = true;
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");

        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        var parent = libraryManager.FindByPath(Path.GetDirectoryName(info.Path)!, true);

        result.Item.Name = episode.Name;
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = localConfiguration.CorrectIndex ?
            (int)episode.Order :
            (int)episode.Order + localConfiguration.Offset;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

        // 通过目录的季id更新季号
        if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            if (season.ProviderIds.TryGetValue(Constants.SeasonNumberProviderName, out var seasonNum)
                && int.TryParse(seasonNum, out var num))
            {
                result.Item.ParentIndexNumber = num;
            }
        }

        // 如果季号为空则使用通过剧集猜测的季号
        if (!result.Item.ParentIndexNumber.HasValue)
        {
            result.Item.ParentIndexNumber = (int?)episode.SeasonNumber ?? 1;
        }

        FillFallbackTitle(result.Item);

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;

        // 获取特典季号用于排序
        var seasonNumber = 1;
        while (parent != null && parent is not Series)
        {
            // 多季度合集中，sp可能位于更深层的目录中，Jellyfin只识别到二级目录
            if (parent is not Season)
            {
                var parentPath = Path.GetDirectoryName(parent!.Path);
                if (string.IsNullOrEmpty(parentPath)) break;
                parent = libraryManager.FindByPath(parentPath, true);
                continue;
            }

            if (parent.ProviderIds.TryGetValue(Constants.SeasonNumberProviderName, out var seasonNum))
            {
                _ = int.TryParse(seasonNum, out seasonNumber);
            }
            break;
        }


        // use title and overview from special episode subject if episode data is empty
        var series = await api.GetSubject(episode.ParentId, cancellationToken);
        if (series == null)
            return result;
        if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
            result.Item.AirsBeforeSeasonNumber = seasonNumber;
        else
            result.Item.AirsAfterSeasonNumber = seasonNumber;

        // 小数集号
        if (episode.Order % 1 != 0)
        {
            result.Item.AirsBeforeEpisodeNumber = ((int)Math.Ceiling(episode.Order));
        }

        return result;

        // 无法刮削到剧集信息时，使用原文件名作为剧集标题
        void FillFallbackTitle(Episode item)
        {
            if (string.IsNullOrEmpty(item.Name))
                item.Name = Path.GetFileNameWithoutExtension(info.Path);
            if (string.IsNullOrEmpty(item.OriginalTitle))
                item.OriginalTitle = Path.GetFileNameWithoutExtension(info.Path);
        }
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
