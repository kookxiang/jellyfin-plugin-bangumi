using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Parser;
using Jellyfin.Plugin.Bangumi.Parser.Anitomy;
using Jellyfin.Plugin.Bangumi.Parser.Basic;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILoggerFactory _loggerFactory;

    public EpisodeProvider(BangumiApi api, ILoggerFactory loggerFactory, ILibraryManager libraryManager,IFileSystem fileSystem)
    {
        _api = api;
        _loggerFactory = loggerFactory;
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
    }

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var _log = _loggerFactory.CreateLogger<EpisodeProvider>();
        var localConfiguration = await LocalConfiguration.ForPath(info.Path);

        EpisodeHandler episodeHandler = new EpisodeHandler(_api, _loggerFactory, _libraryManager, Configuration, info, localConfiguration, token,_fileSystem);
        var episode = await episodeHandler.GetEpisode();
        _log.LogInformation("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        if (episode == null)
            return result;

        result.Item = new Episode();
        result.HasMetadata = true;
        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        result.Item.Name = episode.GetName(Configuration);
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order + localConfiguration.Offset;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

        // 对于 bangumi 中无数据的剧集，如特典，应移除对应 ProviderIds
        if (episode.Id > 0)
            result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");
        else
            result.Item.ProviderIds.Add(Constants.ProviderName, null);

        // 由于特典类型判断条件多，因此当 info.ParentIndexNumber 为 0 时，修改默认值为 1
        result.Item.ParentIndexNumber = info.ParentIndexNumber == null || info.ParentIndexNumber==0 ? 1 : info.ParentIndexNumber;
        _log.LogDebug("Parent index number: {index} in info metadata", info.ParentIndexNumber);

        var parent = _libraryManager.FindByPath(Path.GetDirectoryName(info.Path), true);

        // 默认使用基础规则判断季度类型
        if (!Configuration.AlwaysParseEpisodeByAnitomySharp && BasicEpisodeParser.IsSpecial(info.Path))
            result.Item.ParentIndexNumber = 0;
        
        if (episode.Type != EpisodeType.Normal ||  episode.Id == 0)
        {
            result.Item.ParentIndexNumber = 0;
        }
        else if (parent is Season season)
        {
            result.Item.SeasonId = season.Id;
            result.Item.ParentIndexNumber = season.IndexNumber;
        }

        if (episode.Type == EpisodeType.Normal && result.Item.ParentIndexNumber > 0)
            return result;


        if (episode.ParentId != 0)
        {
            _log.LogDebug("Use series info: {series}", episode.ParentId);
            // use title and overview from special episode subject if episode data is empty
            var series = await _api.GetSubject(episode.ParentId, token);
            if (series == null)
                return result;

            // use title and overview from special episode subject if episode data is empty
            if (string.IsNullOrEmpty(result.Item.Name))
                result.Item.Name = series.GetName(Configuration);
            if (string.IsNullOrEmpty(result.Item.OriginalTitle))
                result.Item.OriginalTitle = series.OriginalName;
            if (string.IsNullOrEmpty(result.Item.Overview))
                result.Item.Overview = series.Summary;

            var seasonNumber = parent is Season ? parent.IndexNumber : 1;
            if (!string.IsNullOrEmpty(episode.AirDate) && string.Compare(episode.AirDate, series.AirDate, StringComparison.Ordinal) < 0)
                result.Item.AirsBeforeEpisodeNumber = seasonNumber;
            else
                result.Item.AirsAfterSeasonNumber = seasonNumber;
        }

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return _api.GetHttpClient().GetAsync(url, token);
    }

}